using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Windows.Controls;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Media;
using System.Windows.Documents;
using System.IO;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Printing;

[assembly: AssemblyVersion("1.0.0.9")]
[assembly: AssemblyFileVersion("1.0.0.9")]
[assembly: AssemblyInformationalVersion("46.0")]

namespace VMS.TPS
{
    public class Script
    {
        // Constantes y parámetros clínicos
        private const double AlphaBetaTumor = 10.0;
        private const double AlphaBetaOAR = 3.0;
        private const double TargetVolumeRel90 = 90.0;
        private const double TargetVolumeAbs2 = 2.0;
        private const double TotalTime = 28.0;
        private const double Tdelay = 28.0;
        private const double K = 0.6;
        private const double TargetEQD2 = 8.0;
        private const double Tolerance = 0.1;

        // Diccionarios de configuración
        private static readonly Dictionary<string, string> StructureMappings = new Dictionary<string, string>
        {
            {"PTV_56", "PTV+CTV"}, {"Recto", "Recto"}, {"Vejiga", "Vejiga"}, {"Sigma", "Sigma"},
            {"HR-CTV", "PTV+CTV"}, {"Recto-HDR", "Recto"}, {"Vejiga-HDR", "Vejiga"}, {"Sigma-HDR", "Sigma"}
        };

        private static readonly Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> Constraints =
            new Dictionary<string, (double, double, string, string, string)>
        {
            { "Recto",    (65.0, 75.0, "lessThan", "< 65 Gy", "< 75 Gy") },
            { "Vejiga",   (80.0, 90.0, "lessThan", "< 80 Gy", "< 90 Gy") },
            { "Sigma",    (70.0, 75.0, "lessThan", "< 70 Gy", "< 75 Gy") },
            { "PTV+CTV",  (85.0, 95.0, "range",    "> 85 Gy", "< 95 Gy") }
        };

        private static readonly Dictionary<string, Brush> StructureColors = new Dictionary<string, Brush>
        {
            {"PTV+CTV", Brushes.SteelBlue}, {"Recto", Brushes.Green},
            {"Vejiga", Brushes.Purple}, {"Sigma", Brushes.Orange}
        };

        public void Execute(ScriptContext context)
        {
            if (context?.Patient == null)
            {
                MessageBox.Show("No hay un paciente cargado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var reportBuilder = new StringBuilder();
            var eqd2Total = InitializeEQD2Totals();
            Dictionary<string, List<double>> btDosesData = null;

            GenerateReportHeader(reportBuilder, context.Patient.Name, context.Patient.Id);

            foreach (var course in context.Patient.Courses)
            {
                if (IsEBRTCourse(course.Id))
                {
                    ProcessEBRTCourse(course, reportBuilder, eqd2Total);
                }
                else if (IsBrachyCourse(course.Id))
                {
                    btDosesData = ProcessBrachyCourse(course, reportBuilder, eqd2Total);
                }
            }

            GenerateTotalSection(reportBuilder, eqd2Total);
            EvaluateTreatmentPlan(reportBuilder, eqd2Total);
            ShowReportWindow(reportBuilder, btDosesData, eqd2Total);
        }

        #region Métodos de Procesamiento

        private Dictionary<string, double> InitializeEQD2Totals()
        {
            return Constraints.Keys.ToDictionary(key => key, _ => 0.0);
        }

        private bool IsEBRTCourse(string courseId)
        {
            return courseId.IndexOf("Cervix", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("EBRT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsBrachyCourse(string courseId)
        {
            return courseId.IndexOf("Braqui", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("Fletcher", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("HDR", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ProcessEBRTCourse(Course course, StringBuilder sb, Dictionary<string, double> eqd2Total)
        {
            sb.AppendLine("\n========================== SECCIÓN EBRT ==========================");
            sb.AppendLine($"║   Esquema: {GetTreatmentScheme(course)}   ║");
            sb.AppendLine("------------------------------------------------------------------");

            var plan28Fx = course.ExternalPlanSetups.FirstOrDefault(p => IsPlanApproved(p) && p.NumberOfFractions == 28);
            if (plan28Fx == null)
            {
                sb.AppendLine("⚠ No se encontró plan aprobado con 28 fracciones");
                return;
            }

            sb.AppendLine($"\nPlan seleccionado: {plan28Fx.Id}");
            sb.AppendLine("----------------------------------------------");
            sb.AppendLine("| Estructura  | Dosis[Gy]    | EQD2 [Gy]      |");
            sb.AppendLine("-----------------------------------------------");

            foreach (var structureId in new[] { "PTV_56", "Recto", "Vejiga", "Sigma" })
            {
                if (!StructureMappings.TryGetValue(structureId, out var key)) continue;

                double volume = structureId == "PTV_56" ? TargetVolumeRel90 : TargetVolumeAbs2;
                double doseAtVolume = GetDoseAtVolume(plan28Fx, structureId, volume);

                if (double.IsNaN(doseAtVolume)) continue;

                double eqd2 = CalculateEQD2ForStructure(doseAtVolume, plan28Fx.NumberOfFractions ?? 0,
                    key == "PTV+CTV" ? AlphaBetaTumor : AlphaBetaOAR);

                sb.AppendLine($"| {structureId,-10} | {doseAtVolume / 100,-7:F2} | {eqd2,-7:F2} |");
                eqd2Total[key] += eqd2;
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
        }

        private Dictionary<string, List<double>> ProcessBrachyCourse(Course course, StringBuilder sb, Dictionary<string, double> eqd2Total)
        {
            var plans = course.BrachyPlanSetups.OrderBy(p => p.Id).ToList();
            if (!plans.Any()) return null;

            sb.AppendLine("\n====================== SECCIÓN HDR-BT ======================");
            sb.AppendLine($"║   Esquema de tratamiento: {GetTreatmentScheme(course)}   ║");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       | EQD2 (Gy)      | Fx #1    | Fx #2    | Fx #3    | Fx #4    | Fx #5    | Total     |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            var btEQD2PerFraction = new Dictionary<string, List<double>>();
            foreach (var structureId in new[] { "HR-CTV", "Recto-HDR", "Vejiga-HDR", "Sigma-HDR" })
            {
                btEQD2PerFraction[structureId] = new List<double>();
                sb.Append($"| {structureId,-15} | {"EQD2",-14} |");

                double totalEQD2 = 0;
                foreach (var plan in plans)
                {
                    double eqd2 = CalculateEQD2ForBrachyFraction(plan, structureId);
                    btEQD2PerFraction[structureId].Add(eqd2);
                    totalEQD2 += eqd2;
                    sb.Append($" {eqd2,-7:F2} |");
                }

                if (StructureMappings.TryGetValue(structureId, out var key))
                {
                    eqd2Total[key] += totalEQD2;
                }
                sb.Append($" {totalEQD2,-7:F2} |\n");
                sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            }

            return btEQD2PerFraction;
        }

        #endregion

        #region Métodos de Cálculo

        private double CalculateEQD2ForStructure(double doseAtVolume, int fractions, double alphaBeta)
        {
            double dosePerFraction = doseAtVolume / 100.0 / fractions;
            double bed = dosePerFraction * fractions * (1 + (dosePerFraction / alphaBeta));
            return bed / (1 + (2.0 / alphaBeta));
        }

        private double CalculateEQD2ForBrachyFraction(BrachyPlanSetup plan, string structureId)
        {
            if (!StructureMappings.TryGetValue(structureId, out var key)) return 0;

            // Asegurarnos de manejar correctamente los valores nullable
            double volume = structureId == "HR-CTV" ?
                plan.StructureSet?.Structures.FirstOrDefault(s => s.Id == structureId)?.Volume * 0.9 ?? TargetVolumeRel90 :
                TargetVolumeAbs2;

            double? nullableDoseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, volume);
            if (!nullableDoseAtVolume.HasValue) return 0;

            double doseAtVolume = nullableDoseAtVolume.Value;
            int fractions = plan.NumberOfFractions.GetValueOrDefault(1); // Valor por defecto si es null

            double dosePerFraction = doseAtVolume / 100.0 / fractions;
            double alphaBeta = key == "PTV+CTV" ? AlphaBetaTumor : AlphaBetaOAR;

            double bed = dosePerFraction * fractions * (1 + (dosePerFraction / alphaBeta));
            bed -= K * (TotalTime - Tdelay);

            return bed / (1 + (2.0 / alphaBeta));
        }

        private double GetDoseAtVolume(PlanSetup plan, string structureId, double volume)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            return structure == null ? double.NaN :
                plan.GetDoseAtVolume(structure, volume,
                    volume == TargetVolumeRel90 ? VolumePresentation.Relative : VolumePresentation.AbsoluteCm3,
                    DoseValuePresentation.Absolute).Dose;
        }

        private double GetDoseAtVolumeAbsoluta(PlanSetup plan, string structureId, double volume)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            return structure == null ? double.NaN :
                plan.GetDoseAtVolume(structure, volume, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute).Dose;
        }

        #endregion

        #region Generación de Reportes

        private void GenerateReportHeader(StringBuilder sb, string patientName, string patientId)
        {
            sb.AppendLine("Resumen Consolidado de Datos EQD2, Ajuste por Tiempo y Evaluación");
            sb.AppendLine("=====================================================================================");
            sb.AppendLine($" Paciente: {patientName}");
            sb.AppendLine($" ID: {patientId}");
            sb.AppendLine($" α/β Tumor: {AlphaBetaTumor}   |   α/β OAR: {AlphaBetaOAR}");
            sb.AppendLine("-------------------------------------------------------------------------------------");
        }

        private void GenerateTotalSection(StringBuilder sb, Dictionary<string, double> eqd2Total)
        {
            sb.AppendLine("\n====================== SECCIÓN TOTAL EBRT + HDR-BT ======================");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       | EQD2 Total (Gy)  | Meta         | Límite       | Concepto Final                     |");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");

            foreach (var item in eqd2Total)
            {
                if (!Constraints.TryGetValue(item.Key, out var constraint)) continue;

                sb.AppendLine($"│ {item.Key,-15} │ {item.Value,14:F2} │ {constraint.aimText,-10} │ {constraint.limitText,-10} │ {EvaluateConstraints(item.Value, constraint),-30} │");
            }
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");
        }

        private string EvaluateConstraints(double eqd2Val, (double aimValue, double limitValue, string type, string aimText, string limitText) constraint)
        {
            double aimVal = constraint.aimValue;
            double limitVal = constraint.limitValue;
            string tipo = constraint.type;

            if (tipo == "lessThan")
            {
                if (eqd2Val <= aimVal)
                    return "✔ APROBADO (Cumple meta)";
                else if (eqd2Val <= limitVal)
                    return "⚠ APROBADO (Dentro límite)";
                else
                    return "✖ NO APROBADO (Supera límite)";
            }
            else if (tipo == "range")
            {
                if (eqd2Val >= aimVal && eqd2Val <= limitVal)
                    return "✔ APROBADO (Rango óptimo)";
                else if (eqd2Val < aimVal)
                    return "⚠ NO APROBADO (Por debajo)";
                else
                    return "✖ NO APROBADO (Excede límite)";
            }
            return "? Sin definición";
        }

        private void EvaluateTreatmentPlan(StringBuilder sb, Dictionary<string, double> eqd2Total)
        {
            bool isApproved = eqd2Total.All(item =>
                !Constraints.TryGetValue(item.Key, out var constraint) ||
                !EvaluateConstraints(item.Value, constraint).Contains("NO APROBADO"));

            sb.AppendLine(isApproved
                ? "\nEl plan de tratamiento cumple con los criterios y ESTÁ APROBADO."
                : "\nEl plan de tratamiento NO cumple con los criterios y NO está aprobado.");
        }

        #endregion

        #region Interfaz de Usuario

        private void ShowReportWindow(StringBuilder sb, Dictionary<string, List<double>> btDosesData, Dictionary<string, double> eqd2Total)
        {
            var window = new Window
            {
                Title = "Resumen Dosimétrico - V46",
                Width = 1200,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var tabControl = new TabControl();

            // Pestaña de Reporte
            var textBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(15),
                TextWrapping = TextWrapping.Wrap
            };
            ProcessColoredText(sb.ToString(), textBlock);

            tabControl.Items.Add(new TabItem
            {
                Header = "Reporte",
                Content = new ScrollViewer { Content = textBlock, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });

            // Pestaña de Gráficos
            if (btDosesData != null && btDosesData.Any())
            {
                tabControl.Items.Add(new TabItem
                {
                    Header = "Gráficos",
                    Content = new ScrollViewer
                    {
                        Content = CreateChartsPanel(btDosesData, eqd2Total),
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    }
                });
            }

            // Panel de botones
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };

            buttonPanel.Children.Add(CreateButton("📤 Exportar a TXT", Brushes.SteelBlue, () => SaveAsTxt(sb)));
            buttonPanel.Children.Add(CreateButton("🖨️ Imprimir Reporte", Brushes.DarkSeaGreen, () => PrintReport(sb.ToString())));
            buttonPanel.Children.Add(CreateButton("Recalcular Dosis", Brushes.DarkOrange, ShowRecalculationDialog));

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            mainGrid.Children.Add(tabControl);
            Grid.SetRow(buttonPanel, 1);
            mainGrid.Children.Add(buttonPanel);

            window.Content = mainGrid;
            window.ShowDialog();
        }

        private StackPanel CreateChartsPanel(Dictionary<string, List<double>> btDosesData, Dictionary<string, double> eqd2Total)
        {
            var chartStack = new StackPanel { Orientation = Orientation.Vertical };

            if (btDosesData.ContainsKey("HR-CTV"))
            {
                chartStack.Children.Add(new GroupBox
                {
                    Header = "EQD2 por Fracción en HR-CTV (α/β=10)",
                    Margin = new Thickness(10),
                    Content = CreateEQD2PerFractionChart(btDosesData["HR-CTV"])
                });
            }

            chartStack.Children.Add(new GroupBox
            {
                Header = "EQD2 Total (EBRT + HDR-BT) con Límites Clínicos",
                Margin = new Thickness(10),
                Content = CreateCombinedChart(btDosesData, eqd2Total, Constraints)
            });

            return chartStack;
        }

        private Canvas CreateCombinedChart(Dictionary<string, List<double>> btEQD2PerFraction,
                                 Dictionary<string, double> eqd2Total,
                                 Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            const double margin = 50;
            const double barWidth = 40;
            const double spacing = 30;
            const double chartHeight = 600;
            const double chartWidth = 1000;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = Brushes.White
            };

            // Calcular escala máxima
            double maxDose = Math.Max(
                btEQD2PerFraction?.Any() == true ? btEQD2PerFraction.Max(x => x.Value.DefaultIfEmpty().Max()) : 0,
                Math.Max(eqd2Total?.Any() == true ? eqd2Total.Max(x => x.Value) : 0, TargetEQD2 * 1.3)
            );

            double scaleFactor = (chartHeight - 2 * margin) / maxDose;

            // Dibujar ejes y líneas de referencia
            DrawAxesWithReferenceLines(canvas, margin, chartHeight, chartWidth, maxDose, scaleFactor);

            // Dibujar barras por fracción (solo HR-CTV)
            if (btEQD2PerFraction?.ContainsKey("HR-CTV") == true)
            {
                double groupWidth = (barWidth + spacing) * 1.5;
                double xStart = margin + spacing;

                for (int i = 0; i < btEQD2PerFraction["HR-CTV"].Count; i++)
                {
                    double xPos = xStart + (i * groupWidth);
                    double eqd2 = btEQD2PerFraction["HR-CTV"][i];
                    double barHeight = eqd2 * scaleFactor;

                    var bar = new Rectangle
                    {
                        Width = barWidth,
                        Height = barHeight,
                        Fill = GetBarColor(eqd2, TargetEQD2 * (1 - Tolerance), TargetEQD2 * (1 + Tolerance)),
                        Stroke = Brushes.Black,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(bar, xPos);
                    Canvas.SetTop(bar, chartHeight - margin - barHeight);
                    canvas.Children.Add(bar);

                    AddTextBlock(canvas, eqd2.ToString("F1"), xPos + (barWidth / 2) - 15,
                        chartHeight - margin - barHeight - 20, 10, FontWeights.Bold);
                    AddTextBlock(canvas, $"Fx {i + 1}", xPos + (barWidth / 2) - 10,
                        chartHeight - margin + 5, 10);
                }
            }

            // Dibujar barras de EQD2 total
            double totalBarsStartX = margin + spacing +
                (btEQD2PerFraction?.ContainsKey("HR-CTV") == true ? btEQD2PerFraction["HR-CTV"].Count * (barWidth + spacing * 3) : 0);
            int colorIndex = 0;

            foreach (var item in eqd2Total)
            {
                if (!constraints.ContainsKey(item.Key)) continue;

                double xPos = totalBarsStartX + (colorIndex * (barWidth + spacing));
                double barHeight = item.Value * scaleFactor;

                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = StructureColors[item.Key],
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(bar, xPos);
                Canvas.SetTop(bar, chartHeight - margin - barHeight);
                canvas.Children.Add(bar);

                AddTextBlock(canvas, item.Value.ToString("F1"), xPos, chartHeight - margin - barHeight - 20, 10);
                AddTextBlock(canvas, item.Key, xPos - 5, chartHeight - margin + 15, 10, transform: new RotateTransform(-45));

                colorIndex++;
            }

            DrawCombinedLegend(canvas, margin);
            return canvas;
        }
        private void DrawAxesWithReferenceLines(Canvas canvas, double margin, double chartHeight,
                              double chartWidth, double maxDose, double scaleFactor)
        {
            // Dibujar ejes principales
            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = chartWidth - margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            });

            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = margin,
                X2 = margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            });

            // Dibujar marcas en el eje Y
            for (double dose = 0; dose <= maxDose; dose += maxDose / 10)
            {
                double yPos = chartHeight - margin - (dose * scaleFactor);

                canvas.Children.Add(new Line
                {
                    X1 = margin - 5,
                    Y1 = yPos,
                    X2 = margin,
                    Y2 = yPos,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                });

                AddTextBlock(canvas, dose.ToString("F1"), margin - 35, yPos - 8, 10);
            }

            // Línea de referencia principal (8 Gy)
            double targetY = chartHeight - margin - (TargetEQD2 * scaleFactor);
            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = targetY,
                X2 = chartWidth - margin,
                Y2 = targetY,
                Stroke = Brushes.Blue,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            });

            // Líneas de tolerancia ±10%
            double upperY = chartHeight - margin - (TargetEQD2 * (1 + Tolerance) * scaleFactor);
            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = upperY,
                X2 = chartWidth - margin,
                Y2 = upperY,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 })
            });

            double lowerY = chartHeight - margin - (TargetEQD2 * (1 - Tolerance) * scaleFactor);
            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = lowerY,
                X2 = chartWidth - margin,
                Y2 = lowerY,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 })
            });

            // Etiquetas de líneas de referencia
            AddTextBlock(canvas, $"{TargetEQD2:F1} Gy (Ref)", margin + 10, targetY - 15, 10, foreground: Brushes.Blue);
            AddTextBlock(canvas, $"+10% ({TargetEQD2 * (1 + Tolerance):F1} Gy)", margin + 10, upperY - 15, 9, foreground: Brushes.Gray);
            AddTextBlock(canvas, $"-10% ({TargetEQD2 * (1 - Tolerance):F1} Gy)", margin + 10, lowerY - 15, 9, foreground: Brushes.Gray);
        }
        private Button CreateButton(string content, Brush background, Action action)
        {
            var button = new Button
            {
                Content = content,
                Margin = new Thickness(5),
                Style = CreateButtonStyle(background)
            };
            button.Click += (s, e) => action();
            return button;
        }

        private Style CreateButtonStyle(Brush background)
        {
            var style = new Style(typeof(Button));

            // Agregar setters individualmente
            style.Setters.Add(new Setter(Control.BackgroundProperty, background));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(5)));
            style.Setters.Add(new Setter(Control.MinWidthProperty, 120.0));

            // Trigger para efecto hover
            var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(255, 70, 130, 180))));
            style.Triggers.Add(trigger);

            return style;
        }

        #endregion

        #region Métodos de Gráficos

        private Canvas CreateEQD2PerFractionChart(List<double> eqd2Values)
        {
            const double margin = 50;
            const double barWidth = 50;
            const double spacing = 30;
            const double chartHeight = 500;
            const double chartWidth = 900;
            const double targetEQD2 = 5.0;
            const double limitEQD2 = 8.0;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = Brushes.White
            };

            // Configurar escala
            double maxDose = Math.Max(eqd2Values.DefaultIfEmpty().Max(), limitEQD2) * 1.2;
            double scaleFactor = (chartHeight - 2 * margin) / maxDose;

            // Dibujar ejes y líneas de referencia
            DrawAxesWithLimits(canvas, margin, chartHeight, chartWidth, maxDose, scaleFactor, targetEQD2, limitEQD2);

            // Dibujar barras para cada fracción
            for (int i = 0; i < eqd2Values.Count; i++)
            {
                double xPos = margin + spacing + (i * (barWidth + spacing));
                double eqd2 = eqd2Values[i];
                double barHeight = eqd2 * scaleFactor;

                // Barra completa
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = GetBarColor(eqd2, targetEQD2, limitEQD2),
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(bar, xPos);
                Canvas.SetTop(bar, chartHeight - margin - barHeight);
                canvas.Children.Add(bar);

                // Etiqueta de valor EQD2
                AddTextBlock(canvas, $"{eqd2:F1} Gy",
                    xPos + (barWidth / 2) - 15,
                    chartHeight - margin - barHeight - 25,
                    11, FontWeights.Bold);

                // Etiqueta de fracción
                AddTextBlock(canvas, $"Fx {i + 1}",
                    xPos + (barWidth / 2) - 10,
                    chartHeight - margin + 5,
                    11);
            }

            // Leyenda mejorada
            DrawLegend(canvas, margin, targetEQD2, limitEQD2);

            return canvas;
        }
        private void DrawAxesWithLimits(Canvas canvas, double margin, double chartHeight,
                      double chartWidth, double maxDose, double scaleFactor,
                      double target, double limit)
        {
            // Eje X
            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = chartWidth - margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            });

            // Eje Y
            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = margin,
                X2 = margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            });

            // Marcas y valores del eje Y
            for (double dose = 0; dose <= maxDose; dose += 2)
            {
                double yPos = chartHeight - margin - (dose * scaleFactor);

                canvas.Children.Add(new Line
                {
                    X1 = margin - 5,
                    Y1 = yPos,
                    X2 = margin,
                    Y2 = yPos,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                });

                AddTextBlock(canvas, dose.ToString("F1"), margin - 30, yPos - 10, 10);
            }

            // Línea de objetivo (5 Gy)
            double targetY = chartHeight - margin - (target * scaleFactor);
            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = targetY,
                X2 = chartWidth - margin,
                Y2 = targetY,
                Stroke = Brushes.Green,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            });

            // Línea de límite (8 Gy)
            double limitY = chartHeight - margin - (limit * scaleFactor);
            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = limitY,
                X2 = chartWidth - margin,
                Y2 = limitY,
                Stroke = Brushes.Red,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            });
        }

        private void DrawLegend(Canvas canvas, double margin, double target, double limit)
        {
            var legend = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
            };

            var legendItems = new List<LegendItem>
    {
        new LegendItem { Color = Brushes.Green, Text = $"≤ {target:F1} Gy (Objetivo)", IsLine = false },
        new LegendItem { Color = Brushes.Orange, Text = $"{target:F1}-{limit:F1} Gy (Aceptable)", IsLine = false },
        new LegendItem { Color = Brushes.Red, Text = $"> {limit:F1} Gy (Excede límite)", IsLine = false },
        new LegendItem { Color = Brushes.Green, Text = $"--- Línea objetivo ({target:F1} Gy)", IsLine = true },
        new LegendItem { Color = Brushes.Red, Text = $"--- Límite máximo ({limit:F1} Gy)", IsLine = true }
    };

            foreach (var item in legendItems)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };

                if (item.IsLine)
                {
                    panel.Children.Add(new Line
                    {
                        X1 = 0,
                        X2 = 30,
                        Y1 = 6,
                        Y2 = 6,
                        Stroke = item.Color,
                        StrokeThickness = 2,
                        StrokeDashArray = item.Color == Brushes.Green ? null : new DoubleCollection(new[] { 5.0, 3.0 })
                    });
                }
                else
                {
                    panel.Children.Add(new Rectangle
                    {
                        Width = 15,
                        Height = 15,
                        Fill = item.Color,
                        Stroke = Brushes.Black,
                        Margin = new Thickness(0, 0, 5, 0)
                    });
                }

                panel.Children.Add(new TextBlock
                {
                    Text = item.Text,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });

                legend.Children.Add(panel);
            }

            Canvas.SetLeft(legend, margin + 20);
            Canvas.SetTop(legend, margin + 20);
            canvas.Children.Add(legend);
        }

        private class LegendItem
        {
            public Brush Color { get; set; }
            public string Text { get; set; }
            public bool IsLine { get; set; }
        }
        private void AddReferenceLine(Canvas canvas, double margin, double chartHeight, double chartWidth,
                             double scaleFactor, double doseValue, Brush color, string labelText)
        {
            double yPos = chartHeight - margin - (doseValue * scaleFactor);

            canvas.Children.Add(new Line
            {
                X1 = margin,
                Y1 = yPos,
                X2 = chartWidth - margin,
                Y2 = yPos,
                Stroke = color,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            });

            AddTextBlock(canvas, labelText, margin + 10, yPos - 15, color == Brushes.Blue ? 10 : 9, foreground: color);
        }

        private void AddTextBlock(Canvas canvas, string text, double left, double top,
                 double fontSize = 12, FontWeight? fontWeight = null,
                 Brush foreground = null, Transform transform = null)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = foreground ?? Brushes.Black
            };

            if (fontWeight.HasValue)
                textBlock.FontWeight = fontWeight.Value;

            if (transform != null)
                textBlock.LayoutTransform = transform;

            Canvas.SetLeft(textBlock, left);
            Canvas.SetTop(textBlock, top);
            canvas.Children.Add(textBlock);
        }

        private Brush GetBarColor(double eqd2, double lowerLimit, double upperLimit)
        {
            if (eqd2 >= lowerLimit && eqd2 <= upperLimit)
                return Brushes.Green; // Dentro del rango ±10%
            else if (eqd2 < lowerLimit)
                return Brushes.Orange; // Por debajo del -10%
            else
                return Brushes.Red; // Por encima del +10%
        }

        private void DrawCombinedLegend(Canvas canvas, double margin)
        {
            var legend = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))
            };

            // Leyenda para fracciones
            var fractionLegend = new StackPanel { Orientation = Orientation.Vertical };
            fractionLegend.Children.Add(new TextBlock
            {
                Text = "EQD2 por Fracción (HR-CTV):",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var fractionItems = new[]
            {
                new { Color = Brushes.Green, Text = $"{TargetEQD2 * (1 - Tolerance):F1}-{TargetEQD2 * (1 + Tolerance):F1} Gy (±10%)" },
                new { Color = Brushes.Orange, Text = $"< {TargetEQD2 * (1 - Tolerance):F1} Gy" },
                new { Color = Brushes.Red, Text = $"> {TargetEQD2 * (1 + Tolerance):F1} Gy" }
            };

            foreach (var item in fractionItems)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                panel.Children.Add(new Rectangle
                {
                    Width = 15,
                    Height = 15,
                    Fill = item.Color,
                    Stroke = Brushes.Black,
                    Margin = new Thickness(0, 0, 5, 0)
                });
                panel.Children.Add(new TextBlock { Text = item.Text, FontSize = 10 });
                fractionLegend.Children.Add(panel);
            }

            // Leyenda para totales
            var totalLegend = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };
            totalLegend.Children.Add(new TextBlock
            {
                Text = "EQD2 Total:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            foreach (var item in StructureColors)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                panel.Children.Add(new Rectangle
                {
                    Width = 15,
                    Height = 15,
                    Fill = item.Value,
                    Stroke = Brushes.Black,
                    Margin = new Thickness(0, 0, 5, 0)
                });
                panel.Children.Add(new TextBlock { Text = item.Key, FontSize = 10 });
                totalLegend.Children.Add(panel);
            }

            legend.Children.Add(fractionLegend);
            legend.Children.Add(totalLegend);

            Canvas.SetLeft(legend, margin + 20);
            Canvas.SetTop(legend, margin + 20);
            canvas.Children.Add(legend);
        }

        #endregion

        #region Métodos Auxiliares

        private string GetTreatmentScheme(Course course)
        {
            if (IsEBRTCourse(course.Id))
            {
                var plan = course.ExternalPlanSetups.FirstOrDefault(p => IsPlanApproved(p));
                return plan != null ? $"{plan.TotalDose.Dose:F1} cGy en {plan.NumberOfFractions} fx" : "Esquema EBRT no especificado";
            }

            if (IsBrachyCourse(course.Id))
            {
                var plan = course.BrachyPlanSetups.FirstOrDefault();
                return plan != null ? $"{plan.TotalDose.Dose * 5:F1} cGy en {plan.NumberOfFractions * 5} fx" : "Esquema HDR-BT no especificado";
            }

            return "Tipo de tratamiento no reconocido";
        }

        private bool IsPlanApproved(PlanSetup plan)
        {
            return plan != null &&
                   (plan.ApprovalStatus == PlanSetupApprovalStatus.Completed ||
                    plan.ApprovalStatus == PlanSetupApprovalStatus.CompletedEarly ||
                    plan.ApprovalStatus == PlanSetupApprovalStatus.Retired ||
                    plan.ApprovalStatus == PlanSetupApprovalStatus.TreatmentApproved) &&
                   (plan is ExternalPlanSetup ? plan.NumberOfFractions == 28 : true);
        }

        private void ProcessColoredText(string text, TextBlock textBlock)
        {
            foreach (var line in text.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
            {
                var run = new Run(line + Environment.NewLine) { Foreground = Brushes.Black };

                if (line.Contains("✔ APROBADO"))
                {
                    run.Foreground = Brushes.DarkGreen;
                    run.Background = Brushes.Honeydew;
                    run.FontWeight = FontWeights.Bold;
                }
                else if (line.Contains("⚠ APROBADO") || line.Contains("⚠ NO APROBADO"))
                {
                    run.Foreground = Brushes.DarkOrange;
                    run.Background = Brushes.LightGoldenrodYellow;
                    run.FontWeight = FontWeights.Bold;
                }
                else if (line.Contains("✖ NO APROBADO"))
                {
                    run.Foreground = Brushes.DarkRed;
                    run.Background = Brushes.MistyRose;
                    run.FontWeight = FontWeights.Bold;
                }
                else if (line.Contains("SECCIÓN EBRT") || line.Contains("SECCIÓN HDR-BT"))
                {
                    run.Foreground = Brushes.White;
                    run.Background = Brushes.SteelBlue;
                    run.FontWeight = FontWeights.Bold;
                }
                else if (line.Contains("║") || line.Contains("═") || line.Contains("┌") || line.Contains("─"))
                {
                    run.Foreground = Brushes.Gray;
                }

                textBlock.Inlines.Add(run);
            }
        }

        private void SaveAsTxt(StringBuilder sb)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Archivo de texto|*.txt",
                Title = "Guardar reporte como TXT",
                FileName = $"Reporte_Consolidado_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(saveDialog.FileName, sb.ToString());
                    MessageBox.Show("Reporte guardado como TXT exitosamente.", "Éxito",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al guardar el archivo:\n{ex.Message}", "Error de Guardado",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PrintReport(string reportText)
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            var document = new FlowDocument
            {
                PageWidth = printDialog.PrintableAreaWidth,
                PageHeight = printDialog.PrintableAreaHeight,
                PagePadding = new Thickness(50),
                ColumnGap = 0,
                ColumnWidth = printDialog.PrintableAreaWidth,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10
            };

            var paragraph = new Paragraph();
            var formattedTextBlock = new TextBlock();

            ProcessColoredText(reportText, formattedTextBlock);

            while (formattedTextBlock.Inlines.Count > 0)
            {
                var inline = formattedTextBlock.Inlines.FirstInline;
                formattedTextBlock.Inlines.Remove(inline);
                paragraph.Inlines.Add(inline);
            }

            document.Blocks.Add(paragraph);

            try
            {
                printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,
                    $"Reporte Dosimétrico {DateTime.Now:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al imprimir el reporte:\n{ex.Message}",
                    "Error de Impresión", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowRecalculationDialog()
        {
            var dialog = new Window
            {
                Title = "Recálculo de Fracciones Restantes",
                Width = 350,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var stackPanel = new StackPanel { Margin = new Thickness(15) };

            // Configuración de controles de entrada...
            // (Mantener la implementación existente)

            dialog.Content = stackPanel;
            dialog.ShowDialog();
        }

        #endregion  
    }

    public class RecalculatedDoseResults
    {
        public int NewFractions { get; set; }
        public double NewDosePerFraction { get; set; }
        public double TotalDose { get; }    
        public double TotalBED { get; set; }
        public double TotalEQD2 { get; set; }
        public double OAR_BED { get; set; }
        public double OAR_EQD2 { get; set; }

        public override string ToString() =>
            $"Fracciones restantes: {NewFractions}\n" +
            $"Nueva dosis/fracción: {NewDosePerFraction:F2} Gy\n" +
            $"Dosis total restante: {TotalDose:F2} Gy\n" +
            $"BED Tumor: {TotalBED:F2} Gy₁₀\n" +
            $"EQD2 Tumor: {TotalEQD2:F2} Gy\n" +
            $"BED OAR: {OAR_BED:F2} Gy₃\n" +
            $"EQD2 OAR: {OAR_EQD2:F2} Gy";
    }
}