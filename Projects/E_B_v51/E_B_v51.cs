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
using System.Windows.Input; // Necesario para Cursors
using System.Printing; // Necesario para PrintDialog

[assembly: AssemblyVersion("1.0.0.9")]
[assembly: AssemblyFileVersion("1.0.0.9")]
[assembly: AssemblyInformationalVersion("40.0")]

namespace VMS.TPS
{
    public class Script
    {
        //----------------------------------------------------------------------------------------------------------------------
        // Constantes y parámetros clínicos
        //----------------------------------------------------------------------------------------------------------------------
        private const double alphaBetaTumor = 10.0;
        private const double alphaBetaOAR = 3.0;
        private const double targetVolumeRel90 = 90.0; // 90% para PTV/CTV
        private const double targetVolumeAbs2 = 2.0;   // 2cc para OARs
        private const double totalTime = 28.0;        // Tiempo total de tratamiento (días)
        private const double Tdelay = 28.0;          // Tiempo de retraso para repoblación
        private const double k = 0.6;                // Constante de repoblación
        private const double BrachyDoseReference = 6.0;    // 6 Gy de referencia
        private const double BrachyDoseTolerance = 0.1;    // ±10% de tolerancia
        //----------------------------------------------------------------------------------------------------------------------
        // Método principal de ejecución
        //----------------------------------------------------------------------------------------------------------------------
        public void Execute(ScriptContext context)
        {
            if (context?.Patient == null)
            {
                MessageBox.Show("No hay un paciente cargado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Mostrar diálogo de selección de PTV
            var ptvDialog = new PTVSelectionDialog();
            if (ptvDialog.ShowDialog() == true)
            {
                _selectedPTV = ptvDialog.SelectedPTV;
            }
            else
            {
                return; // Cancelar ejecución si se cierra el diálogo
            }

            // Inicialización de estructuras de datos
            var reportBuilder = new StringBuilder();
            var (patientName, patientId) = (context.Patient.Name, context.Patient.Id);

            // Diccionarios para acumulación y restricciones
            var eqd2Totals = new Dictionary<string, double>
            {
                ["PTV+CTV"] = 0,
                ["Recto"] = 0,
                ["Vejiga"] = 0,
                ["Sigma"] = 0
            };

            // VALORES DE REFERENCIA ICRU 89
            var constraints = new Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)>
            {
                ["Recto"] = (90.0, 64.3, "lessThan", "≤ 90.0 Gy", "≤ 64.3 Gy"),
                ["Vejiga"] = (70.0, 80.6, "lessThan", "≤ 70 Gy", "≤ 80.6 Gy"),
                ["Sigma"] = (75.0, 51.7, "lessThan", "≤ 75 Gy", "≤ 51.7 Gy"),
                ["PTV+CTV"] = (85.0, 92.3, "range", "≥ 85 Gy", "≤ 92.3 Gy")
            };

            // Variables para acumulación de dosis
            var (totalDosisEBRT, totalDosisBT, totalFraccionesEBRT, totalFraccionesBT) = (0.0, 0.0, 0, 0);
            Dictionary<string, List<double>> btDosesData = null;

            // Generación del encabezado
            GenerateReportHeader(reportBuilder, patientName, patientId);

            // Procesamiento de cursos
            foreach (var course in context.Patient.Courses)
            {
                if (IsEBRTCourse(course.Id))
                {
                    ProcessEBRTCourse(course, reportBuilder, ref totalDosisEBRT, ref totalFraccionesEBRT, eqd2Totals);
                }
                else if (IsBrachyCourse(course.Id))
                {
                    btDosesData = ProcessBrachyCourse(course, reportBuilder, ref totalDosisBT, ref totalFraccionesBT, eqd2Totals);
                }
            }

            // Generación de secciones finales
            GenerateTotalSection(reportBuilder, eqd2Totals, constraints);
            EvaluateTreatmentPlan(reportBuilder, eqd2Totals, constraints);
            ShowReportWindow(reportBuilder, btDosesData, eqd2Totals, constraints, totalDosisEBRT, totalFraccionesBT);
        }
        //----------------------------------------------------------------------------------------------------------------------
        // Métodos auxiliares para procesamiento de cursos
        //----------------------------------------------------------------------------------------------------------------------
        private bool IsEBRTCourse(string courseId)
        {
            if (string.IsNullOrEmpty(courseId)) return false;

            return courseId.IndexOf("EBRT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("Cervix", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private bool IsBrachyCourse(string courseId)
        {
            if (string.IsNullOrEmpty(courseId)) return false;

            return courseId.IndexOf("Braqui", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("Fletcher", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("HDR", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        //----------------------------------------------------------------------------------------------------------------------
        // Procesamiento de EBRT
        //----------------------------------------------------------------------------------------------------------------------
        private void ProcessEBRTCourse(Course course, StringBuilder reportBuilder,
        ref double totalDosisEBRT, ref int totalFraccionesEBRT, Dictionary<string, double> eqd2Total)
        {
            // Get treatment scheme and add header
            reportBuilder.AppendLine("\n============= SECCIÓN EBRT ===============")
                        .AppendLine($"║   Esquema: {GetTreatmentScheme(course)}   ║")
                        .AppendLine("---------------------------------------------");

            // Find approved 28-fraction plan
            var plan28Fx = course.ExternalPlanSetups?
                .FirstOrDefault(p => IsPlanApproved(p) && p.NumberOfFractions == 28);

            if (plan28Fx == null)
            {
                reportBuilder.AppendLine("⚠ No se encontró plan aprobado con 28 fracciones");
                return;
            }

            // Initialize basic plan data
            totalFraccionesEBRT = plan28Fx.NumberOfFractions ?? 0;
            totalDosisEBRT = plan28Fx.TotalDose.Dose;

            // Add plan information header
            // Usar la dosis objetivo correcta según el PTV seleccionado
            double targetDose = _selectedPTV == "PTV_56" ? DosePTV56 : DosePTV50_4;

            reportBuilder.AppendLine($"\nPlan seleccionado: {plan28Fx.Id} (Dosis objetivo: {targetDose} Gy)")
                        .AppendLine("----------------------------------------")
                        .AppendLine("| Estructura |  Dosis[Gy] |  EQD2 [Gy]  |")
                        .AppendLine("----------------------------------------");

            // Process each structure
            foreach (var (structureId, volume, alphaBeta, key) in GetStructureDefinitions())
            {
                double doseAtVolume = GetStructureDose(plan28Fx, structureId, volume,
                    volume == targetVolumeAbs2);

                if (IsValidDose(doseAtVolume))
                {
                    var (eqd2, bed) = CalculateDoseMetrics(doseAtVolume, totalFraccionesEBRT, alphaBeta);

                    reportBuilder.AppendLine($"| {structureId,-10} | {doseAtVolume / 100,-10:F2} | {eqd2,-10:F2} |");
                    eqd2Total[key] += eqd2;

                    if (key == "PTV+CTV")
                    {
                        totalDosisEBRT = eqd2;
                    }
                }
            }

            reportBuilder.AppendLine("---------------------------------------------");
        }

        // Métodos auxiliares extraídos del método principal
        private IEnumerable<(string structureId, double volume, double alphaBeta, string key)> GetStructureDefinitions()
        {
            return new[] {
        (_selectedPTV, targetVolumeRel90, alphaBetaTumor, "PTV+CTV"),
        ("Recto", targetVolumeAbs2, alphaBetaOAR, "Recto"),
        ("Vejiga", targetVolumeAbs2, alphaBetaOAR, "Vejiga"),
        ("Sigma", targetVolumeAbs2, alphaBetaOAR, "Sigma")
    };
        }

        private double GetStructureDose(ExternalPlanSetup plan, string structureId, double volume, bool isAbsoluteVolume)
        {
            return isAbsoluteVolume ?
                GetDoseAtVolumeAbsoluta(plan, structureId, volume) :
                GetDoseAtVolume(plan, structureId, volume);
        }

        private static bool IsValidDose(double dose) => !double.IsNaN(dose) && dose > 0;

        private (double eqd2, double bed) CalculateDoseMetrics(double doseAtVolume, int fractions, double alphaBeta)
        {
            double dosePerFraction = doseAtVolume / 100.0 / fractions;
            double targetDose = _selectedPTV == "PTV_56" ? DosePTV56 : DosePTV50_4;

            // Ajustar cálculo si es necesario para considerar la dosis objetivo
            double bed = CalculateBED(dosePerFraction, fractions, alphaBeta);
            double eqd2 = CalculateEQD2(bed, alphaBeta);

            return (eqd2, bed);
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Procesamiento de Braquiterapia
        //----------------------------------------------------------------------------------------------------------------------
        private Dictionary<string, List<double>> ProcessBrachyCourse(Course course, StringBuilder reportBuilder,
        ref double totalDosisBT, ref int totalFraccionesBT, Dictionary<string, double> eqd2Total)
        {
            var plans = course.BrachyPlanSetups?.OrderBy(p => p.Id).ToList();
            if (plans == null || !plans.Any()) return null;

            // Inicializar reporte
            reportBuilder.AppendLine("\n================================== SECCIÓN HDR-BT =========================================")
                        .AppendLine($"║   Esquema de tratamiento: {GetTreatmentScheme(course)}   ║")
                        .AppendLine("----------------------------------------------------------------------------------------------")
                        .AppendLine("| Estructura      | Métrica      | Fx #1   | Fx #2   | Fx #3   | Fx #4   | Fx #5   | Total   |")
                        .AppendLine("----------------------------------------------------------------------------------------------");

            // Definición de estructuras
            var structures = new[] {
                ("HR-CTV", targetVolumeRel90, alphaBetaTumor, "PTV+CTV", "D90% [Gy]"),
                ("Recto-HDR", targetVolumeAbs2, alphaBetaOAR, "Recto", "D2cc [Gy]"),
                ("Vejiga-HDR", targetVolumeAbs2, alphaBetaOAR, "Vejiga", "D2cc [Gy]"),
                ("Sigma-HDR", targetVolumeAbs2, alphaBetaOAR, "Sigma", "D2cc [Gy]")
            };

            var btDosesPerFraction = structures.ToDictionary(
                s => s.Item1,
                _ => new List<double>(plans.Count)
            );

            // Procesar cada estructura
            foreach (var structure in structures)
            {
                string structureId = structure.Item1;
                double defaultVolume = structure.Item2;
                double alphaBeta = structure.Item3;
                string key = structure.Item4;
                string metric = structure.Item5;

                // Procesar dosis físicas
                var physicalDosesResult = ProcessPhysicalDoses(plans, structureId, defaultVolume, maxFractions: 5);
                List<double> fractionDoses = physicalDosesResult.doses;
                double totalDose = physicalDosesResult.total;

                btDosesPerFraction[structureId] = fractionDoses.Take(plans.Count).ToList();

                // Validación de dosis física
                //string validationResult = ValidateBrachyDose(fractionDoses.FirstOrDefault());

                reportBuilder.Append($"| {structureId,-15} | {metric,-12} |");
                foreach (var dose in fractionDoses)
                {
                    reportBuilder.Append($" {dose,-7:F2} |");
                }
                reportBuilder.Append($" {totalDose,-7:F2} |\n");

                // Procesar valores EQD2
                var eqd2ValuesResult = ProcessEQD2Values(plans, structureId, defaultVolume, alphaBeta, maxFractions: 5);
                List<double> eqd2Values = eqd2ValuesResult.eqd2Values;
                double totalEQD2 = eqd2ValuesResult.total;

                eqd2Total[key] += totalEQD2;

                reportBuilder.Append($"| {"",-15} | {"EQD2 [Gy]",-12} |");
                foreach (var eqd2 in eqd2Values)
                {
                    reportBuilder.Append($" {eqd2,-7:F2} |");
                }
                reportBuilder.Append($" {totalEQD2,-7:F2} |\n")
                        .AppendLine("-----------------------------------------------------------------------------------------------");

            }

            return btDosesPerFraction;
        }

        private (List<double> doses, double total) ProcessPhysicalDoses(List<BrachyPlanSetup> plans, string structureId, double defaultVolume, int maxFractions)
        {
            var fractionDoses = new List<double>();
            double totalDose = 0;

            foreach (var plan in plans)
            {
                double volumeToUse = GetStructureVolume(plan, structureId, defaultVolume);
                double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, volumeToUse) / 100.0;
                fractionDoses.Add(doseAtVolume);
                totalDose += doseAtVolume;
            }

            // Pad with zeros up to maxFractions
            while (fractionDoses.Count < maxFractions)
            {
                fractionDoses.Add(0);
            }

            return (doses: fractionDoses, total: totalDose);
        }

        private (List<double> eqd2Values, double total) ProcessEQD2Values(List<BrachyPlanSetup> plans, string structureId, double defaultVolume, double alphaBeta, int maxFractions)
        {
            var eqd2Values = new List<double>();
            double totalEQD2 = 0;

            foreach (var plan in plans)
            {
                double volumeToUse = GetStructureVolume(plan, structureId, defaultVolume);
                double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, volumeToUse);
                double dosePerFraction = doseAtVolume / 100.0 / (double)plan.NumberOfFractions;
                double bed = CalculateBEDWithTimeAdjustment(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta, totalTime, Tdelay, k);
                double eqd2 = CalculateEQD2(bed, alphaBeta);

                eqd2Values.Add(eqd2);
                totalEQD2 += eqd2;
            }

            // Pad with zeros up to maxFractions
            while (eqd2Values.Count < maxFractions)
            {
                eqd2Values.Add(0);
            }

            return (eqd2Values: eqd2Values, total: totalEQD2);
        }

        private double GetStructureVolume(BrachyPlanSetup plan, string structureId, double defaultVolume)
        {
            if (structureId != "HR-CTV") return defaultVolume;

            var estructura = plan.StructureSet?.Structures.FirstOrDefault(s => s.Id == structureId);
            return estructura?.Volume * 0.9 ?? defaultVolume;
        }

        //**********************
        private Canvas CreateCombinedChart(Dictionary<string, List<double>> btEQD2PerFraction,
                                 Dictionary<string, double> eqd2Total,
                                 Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            //----------------------************
            const double margin = 50;
            const double barWidth = 40;
            const double spacing = 30;
            const double chartHeight = 600;
            const double chartWidth = 1000;
            const double targetEQD2 = 8.0;
            const double tolerance = 0.1;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = Brushes.White
            };
            // Configurar escala - VERSIÓN CORREGIDA
            double maxFromFractions = btEQD2PerFraction.Any() ?
                btEQD2PerFraction.Max(x => x.Value.DefaultIfEmpty().Max()) : 0;
            double maxFromTotals = eqd2Total.Any() ?
                eqd2Total.Max(x => x.Value) : 0;

            double maxDose = Math.Max(
                Math.Max(maxFromFractions, maxFromTotals),
                targetEQD2 * 1.3
            );
            double scaleFactor = (chartHeight - 2 * margin) / maxDose;

            // Dibujar ejes y líneas de referencia
            DrawAxesWithReferenceLines(canvas, margin, chartHeight, chartWidth, maxDose, scaleFactor, targetEQD2, tolerance);
            // Dibujar barras por fracción (solo HR-CTV)
            if (btEQD2PerFraction.ContainsKey("HR-CTV"))
            {
                double groupWidth = (barWidth + spacing) * 1.5;
                double xStart = margin + spacing;

                for (int i = 0; i < btEQD2PerFraction["HR-CTV"].Count; i++)
                {
                    double xPos = xStart + (i * groupWidth);
                    double eqd2 = btEQD2PerFraction["HR-CTV"][i];
                    double barHeight = eqd2 * scaleFactor;

                    // Barra de fracción
                    var bar = new Rectangle
                    {
                        Width = barWidth,
                        Height = barHeight,
                        Fill = GetBarColor(eqd2, targetEQD2 * (1 - tolerance), targetEQD2 * (1 + tolerance)),
                        Stroke = Brushes.Black,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(bar, xPos);
                    Canvas.SetTop(bar, chartHeight - margin - barHeight);
                    canvas.Children.Add(bar);

                    // Etiqueta de valor
                    var valueLabel = new TextBlock
                    {
                        Text = eqd2.ToString("F1"),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black
                    };
                    Canvas.SetLeft(valueLabel, xPos + (barWidth / 2) - 15);
                    Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 20);
                    canvas.Children.Add(valueLabel);

                    // Etiqueta de fracción
                    var fxLabel = new TextBlock
                    {
                        Text = $"Fx {i + 1}",
                        FontSize = 10,
                        Foreground = Brushes.Black
                    };
                    Canvas.SetLeft(fxLabel, xPos + (barWidth / 2) - 10);
                    Canvas.SetTop(fxLabel, chartHeight - margin + 5);
                    canvas.Children.Add(fxLabel);
                }
            }

            // Dibujar barras de EQD2 total por estructura
            double totalBarsStartX = margin + spacing + (btEQD2PerFraction["HR-CTV"].Count * (barWidth + spacing * 3));
            int colorIndex = 0;
            var colors = new[] { Brushes.SteelBlue, Brushes.Green, Brushes.Purple, Brushes.Orange };

            foreach (var item in eqd2Total)
            {
                if (!constraints.ContainsKey(item.Key)) continue;

                double xPos = totalBarsStartX + (colorIndex * (barWidth + spacing));
                double barHeight = item.Value * scaleFactor;

                // Barra total
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = colors[colorIndex % colors.Length],
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(bar, xPos);
                Canvas.SetTop(bar, chartHeight - margin - barHeight);
                canvas.Children.Add(bar);

                // Etiquetas
                var valueLabel = new TextBlock
                {
                    Text = item.Value.ToString("F1"),
                    FontSize = 10,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(valueLabel, xPos);
                Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 20);
                canvas.Children.Add(valueLabel);

                var structLabel = new TextBlock
                {
                    Text = item.Key,
                    FontSize = 10,
                    Foreground = Brushes.Black,
                    LayoutTransform = new RotateTransform(-45)
                };
                Canvas.SetLeft(structLabel, xPos - 5);
                Canvas.SetTop(structLabel, chartHeight - margin + 15);
                canvas.Children.Add(structLabel);

                colorIndex++;
            }

            // Leyenda unificada
            DrawCombinedLegend(canvas, margin, targetEQD2, tolerance);

            return canvas;
        }

        private void DrawAxesWithReferenceLines(Canvas canvas, double margin, double chartHeight,
                                      double chartWidth, double maxDose, double scaleFactor,
                                      double target, double tolerance)
        {
            // Ejes principales
            var xAxis = new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = chartWidth - margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            canvas.Children.Add(xAxis);

            var yAxis = new Line
            {
                X1 = margin,
                Y1 = margin,
                X2 = margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            canvas.Children.Add(yAxis);

            // Línea de referencia principal (5 Gy)
            double targetY = chartHeight - margin - (target * scaleFactor);
            var targetLine = new Line
            {
                X1 = margin,
                Y1 = targetY,
                X2 = chartWidth - margin,
                Y2 = targetY,
                Stroke = Brushes.Blue,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            };
            canvas.Children.Add(targetLine);

            // Líneas de tolerancia ±10%
            double upperY = chartHeight - margin - (target * (1 + tolerance) * scaleFactor);
            var upperLine = new Line
            {
                X1 = margin,
                Y1 = upperY,
                X2 = chartWidth - margin,
                Y2 = upperY,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 })
            };
            canvas.Children.Add(upperLine);

            double lowerY = chartHeight - margin - (target * (1 - tolerance) * scaleFactor);
            var lowerLine = new Line
            {
                X1 = margin,
                Y1 = lowerY,
                X2 = chartWidth - margin,
                Y2 = lowerY,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 })
            };
            canvas.Children.Add(lowerLine);

            // Etiquetas de líneas de referencia
            var targetLabel = new TextBlock
            {
                Text = $"{target:F1} Gy (Ref)",
                FontSize = 10,
                Foreground = Brushes.Blue
            };
            Canvas.SetLeft(targetLabel, margin + 10);
            Canvas.SetTop(targetLabel, targetY - 15);
            canvas.Children.Add(targetLabel);

            var upperLabel = new TextBlock
            {
                Text = $"+10% ({target * (1 + tolerance):F1} Gy)",
                FontSize = 9,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(upperLabel, margin + 10);
            Canvas.SetTop(upperLabel, upperY - 15);
            canvas.Children.Add(upperLabel);

            var lowerLabel = new TextBlock
            {
                Text = $"-10% ({target * (1 - tolerance):F1} Gy)",
                FontSize = 9,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(lowerLabel, margin + 10);
            Canvas.SetTop(lowerLabel, lowerY - 15);
            canvas.Children.Add(lowerLabel);
        }
        //***********////**//***//**/

        private Brush GetBarColor(double eqd2, double lowerLimit, double upperLimit)
        {
            if (eqd2 >= lowerLimit && eqd2 <= upperLimit)
                return Brushes.Green; // Dentro del rango ±10%
            else if (eqd2 < lowerLimit)
                return Brushes.Orange; // Por debajo del -10%
            else
                return Brushes.Red; // Por encima del +10%
        }
        private void DrawCombinedLegend(Canvas canvas, double margin, double target, double tolerance)
        {
            var legend = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                //Padding = new Thickness(5)
            };

            var textTab = new TabItem { Header = "Reporte" };
            var textBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(15),
                TextWrapping = TextWrapping.Wrap
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
                new { Color = Brushes.Green, Text = $"{target * (1 - tolerance):F1}-{target * (1 + tolerance):F1} Gy (±10%)" },
                new { Color = Brushes.Orange, Text = $"< {target * (1 - tolerance):F1} Gy" },
                new { Color = Brushes.Red, Text = $"> {target * (1 + tolerance):F1} Gy" }
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

            var totalItems = new[]
            {
                new { Color = Brushes.SteelBlue, Text = "PTV+CTV" },
                new { Color = Brushes.Green, Text = "Recto" },
                new { Color = Brushes.Purple, Text = "Vejiga" },
                new { Color = Brushes.Orange, Text = "Sigma" }
            };

            foreach (var item in totalItems)
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
                totalLegend.Children.Add(panel);
            }

            // Líneas de referencia
            var refLegend = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };
            refLegend.Children.Add(new TextBlock
            {
                Text = "Líneas de Referencia:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var refItems = new[]
            {
        new { Color = Brushes.Blue, Text = $"{target:F1} Gy (Referencia)", IsLine = true },
        new { Color = Brushes.Gray, Text = $"±10% Tolerancia", IsLine = true }
    };

            foreach (var item in refItems)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                panel.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = 20,
                    Y1 = 6,
                    Y2 = 6,
                    Stroke = item.Color,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 }),
                    Margin = new Thickness(0, 0, 5, 0)
                });
                panel.Children.Add(new TextBlock { Text = item.Text, FontSize = 10 });
                refLegend.Children.Add(panel);
            }

            legend.Children.Add(fractionLegend);
            legend.Children.Add(totalLegend);
            legend.Children.Add(refLegend);

            Canvas.SetLeft(legend, margin + 20);
            Canvas.SetTop(legend, margin + 20);
            canvas.Children.Add(legend);
        }


        //----------------------------------------------------------------------------------------------------------------------
        // Método para presentar esquema de tratamiento
        //----------------------------------------------------------------------------------------------------------------------
        private string GetTreatmentScheme(Course course)
        {
            // Para cursos de EBRT
            if (IsEBRTCourse(course.Id))
            {
                var firstPlan = course.ExternalPlanSetups.FirstOrDefault(p => IsPlanApproved(p));
                if (firstPlan != null)
                {
                    int fractions = firstPlan.NumberOfFractions ?? 0;
                    double totalDose = firstPlan.DosePerFraction.Dose * fractions;
                    //string technique = firstPlan.Beams.Any(b => b.MLC != null) ? "VMAT" : "3D-CRT";

                    return $"{totalDose:F1} cGy en {fractions} fx";
                }
                return "Esquema EBRT no especificado";
            }
            // Para cursos de braquiterapia
            else if (IsBrachyCourse(course.Id))
            {
                var firstPlan = course.BrachyPlanSetups.FirstOrDefault();
                if (firstPlan != null)
                {
                    int fractions = firstPlan.NumberOfFractions ?? 0;
                    double totalDose = firstPlan.DosePerFraction.Dose * fractions;
                    //string applicator = firstPlan.ApplicatorType ?? "Aplicador no especificado";

                    return $"{totalDose * 5:F1} cGy en {fractions * 5} fx";
                }
                return "Esquema HDR-BT no especificado";
            }

            return "Tipo de tratamiento no reconocido";
        }
        //----------------------------------------------------------------------------------------------------------------------
        // Métodos de cálculo de dosis
        //----------------------------------------------------------------------------------------------------------------------
        private double CalculateBED(double dosePerFraction, double fractions, double alphaBeta)
        {
            return (dosePerFraction) * fractions * (1 + (dosePerFraction / alphaBeta));
        }

        private double CalculateBEDWithTimeAdjustment(double dosePerFraction, double fractions, double alphaBeta, double totalTime, double Tdelay, double k)
        {
            double bed = CalculateBED(dosePerFraction, fractions, alphaBeta);
            bed -= k * (totalTime - Tdelay);
            return bed;
        }

        private double CalculateEQD2(double bed, double alphaBeta)
        {
            return bed / (1 + (2.0 / alphaBeta));
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos para obtener dosis en volúmenes
        //----------------------------------------------------------------------------------------------------------------------
        private double GetDoseAtVolume(PlanSetup plan, string structureId, double volume)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            if (structure == null) return 0;
            return plan.GetDoseAtVolume(structure, volume, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose;
        }

        private double GetDoseAtVolumeAbsoluta(PlanSetup plan, string structureId, double volume)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            if (structure == null) return 0;
            return plan.GetDoseAtVolume(structure, volume, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute).Dose;
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos para generación de reportes
        //----------------------------------------------------------------------------------------------------------------------
        
        private void GenerateReportHeader(StringBuilder sb, string patientName, string patientId)
        {
            sb.AppendLine("FUNDACIÓN VALLE DEL LILI");
            sb.AppendLine("Informe de tratamiento");
            sb.AppendLine("Consolidado EQD2 EBRT+BT, ajuste por tiempo y evaluación del plan");
            sb.AppendLine("=====================================================================================");
            sb.AppendLine($" Paciente: {patientName}");
            sb.AppendLine($" ID: {patientId}");
            sb.AppendLine($" Estructura objetivo: {_selectedPTV}");
            sb.AppendLine($" α/β Tumor: {alphaBetaTumor}   |   α/β OAR: {alphaBetaOAR}");
            sb.AppendLine("-------------------------------------------------------------------------------------");
        }

        private void GenerateTotalSection(StringBuilder sb, Dictionary<string, double> eqd2Total,
            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            sb.AppendLine("\n================================== SECCIÓN TOTAL EBRT + HDR-BT ==================================")
            .AppendLine("---------------------------------------------------------------------------------------------------")
            .AppendLine("| Estructura      |EQD2 Total (Gy) | Meta       | Límite     |     Concepto Final               |")
            .AppendLine("---------------------------------------------------------------------------------------------------");

            foreach (var item in eqd2Total)
            {
                string structureId = item.Key;
                double eqd2Val = item.Value;

                if (constraints.TryGetValue(structureId, out var constraint))
                {
                    string concepto = EvaluateConstraints(eqd2Val, constraint);
                    sb.AppendLine($"│ {structureId,-15} │ {eqd2Val,14:F2} │ {constraint.aimText,-10} │ {constraint.limitText,-10} │ {concepto,-30} │");
                }
                else
                {
                    sb.AppendLine($"| {structureId,-15} | {eqd2Val,-16:F2} |      -      |      -      |       Sin definición        |");
                }
            }
            sb.AppendLine("-------------------------------------------------------------------------------------------------");
        }

        private string EvaluateConstraints(double eqd2Val, (double aimValue, double limitValue, string type, string aimText, string limitText) constraint)
        {
            double aimVal = constraint.aimValue;
            double limitVal = constraint.limitValue;
            string tipo = constraint.type;

            if (tipo == "lessThan")
            {
                if (eqd2Val <= aimVal || eqd2Val <= limitVal)
                    return "✔ APROBADO";
                else
                    return "✖ NO APROBADO";
            }
            else if (tipo == "range")
            {
                if (eqd2Val >= aimVal && eqd2Val <= limitVal)
                    return "✔ APROBADO";
                else 
                    return "✖ NO APROBADO";
            }
            return "? Sin definición";
        }

        private void EvaluateTreatmentPlan(StringBuilder sb, Dictionary<string, double> eqd2Total,
            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            if (IsTreatmentPlanApproved(eqd2Total, constraints))
            {
                sb.AppendLine("\nEl plan de tratamiento cumple con los criterios y ESTÁ APROBADO.");
            }
            else
            {
                sb.AppendLine("\nEl plan de tratamiento NO cumple con los criterios y NO está aprobado.");
            }
        }

        private bool IsTreatmentPlanApproved(Dictionary<string, double> eqd2Total,
            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            foreach (var item in eqd2Total)
            {
                string structureId = item.Key;
                double eqd2Val = item.Value;
                if (constraints.TryGetValue(structureId, out var constraint))
                {
                    string evaluacion = EvaluateConstraints(eqd2Val, constraint);
                    if (evaluacion.Contains("NO APROBADO"))
                        return false;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos auxiliares
        //----------------------------------------------------------------------------------------------------------------------
        private bool IsPlanApproved(PlanSetup plan)
        {
            return plan.ApprovalStatus == PlanSetupApprovalStatus.Completed ||
                   plan.ApprovalStatus == PlanSetupApprovalStatus.CompletedEarly ||
                   plan.ApprovalStatus == PlanSetupApprovalStatus.Retired ||
                   plan.ApprovalStatus == PlanSetupApprovalStatus.TreatmentApproved &&
                   plan.NumberOfFractions == 28;
        }

        private void ProcessColoredText(string text, TextBlock textBlock)
        {
            var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                // Manejar HTML simple para las validaciones
                if (line.Contains("<span style="))
                {
                    var htmlParts = line.Split(new[] { "<span", "</span>" }, StringSplitOptions.None);
                    foreach (var part in htmlParts)
                    {
                        if (part.Contains("color:green"))
                        {
                            var run = new Run(part.Replace("style='color:green;font-weight:bold'>", ""))
                            {
                                Foreground = Brushes.Green,
                                FontWeight = FontWeights.Bold
                            };
                            textBlock.Inlines.Add(run);
                        }
                        else if (part.Contains("color:red"))
                        {
                            var run = new Run(part.Replace("style='color:red;font-weight:bold'>", ""))
                            {
                                Foreground = Brushes.Red,
                                FontWeight = FontWeights.Bold
                            };
                            textBlock.Inlines.Add(run);
                        }
                        else if (!string.IsNullOrWhiteSpace(part))
                        {
                            textBlock.Inlines.Add(new Run(part));
                        }
                    }
                    textBlock.Inlines.Add(new Run(Environment.NewLine));
                }
                else
                {
                    // Procesamiento normal del texto
                    var run = new Run(line + Environment.NewLine)
                    {
                        Foreground = Brushes.Black
                    };

                    // Aplicar esquema de semáforo solo a evaluaciones finales
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
                    // Estilos para encabezados (se mantienen visibles pero discretos)
                    else if (line.Contains("SECCIÓN EBRT") || line.Contains("SECCIÓN HDR-BT"))
                    {
                        run.Foreground = Brushes.White;
                        run.Background = Brushes.SteelBlue;
                        run.FontWeight = FontWeights.Bold;
                    }
                    // Bordes de tablas en gris
                    else if (line.Contains("║") || line.Contains("═") || line.Contains("┌") || line.Contains("─"))
                    {
                        run.Foreground = Brushes.Gray;
                    }

                    textBlock.Inlines.Add(run);
                }
            }
        }
        private void SaveAsTxt(StringBuilder sb)
        {
            string patientId = "ID_NoDisp";
            // Intentar extraer ID del reporte de forma segura
            try
            {
                string idLine = sb.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                                    .FirstOrDefault(line => line.Trim().StartsWith("ID:"));
                if (idLine != null)
                {
                    patientId = idLine.Split(':').LastOrDefault()?.Trim() ?? patientId;
                    // Remover caracteres inválidos para nombres de archivo
                    foreach (char invalidChar in System.IO.Path.GetInvalidFileNameChars())
                    {
                        patientId = patientId.Replace(invalidChar, '_');
                    }
                }
            }
            catch { /* Ignorar error de parsing */ }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Archivo de texto|*.txt",
                Title = "Guardar reporte como TXT",
                FileName = $"Reporte_Consolidado_{patientId}_{DateTime.Now:yyyyMMdd_HHmm}.txt"
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

        // Método para imprimir el reporte
        private void PrintReport(string reportText)
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
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

                var formattedTextBlock = new TextBlock();
                ProcessColoredText(reportText, formattedTextBlock);
                var paragraph = new Paragraph();

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
        }

        private Style CreateButtonStyle(Brush background)
        {
            var style = new Style(typeof(Button));
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



        //----------------------------------------------------------------------------------------------------------------------
        // Métodos de generación de gráficos y reporte
        //----------------------------------------------------------------------------------------------------------------------
        private void ShowReportWindow(StringBuilder sb, Dictionary<string, List<double>> btDosesPerFraction,
                    Dictionary<string, double> eqd2Total,
                    Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints, double totalDosisEBRT, int totalFraccionesBT)
        {
            var window = new Window
            {
                Title = "Resumen Dosimétrico - V50",
                Width = 1000,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tabControl = new TabControl();

            // Pestaña de Reporte
            var textTab = new TabItem { Header = "Reporte" };
            var textBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(15),
                TextWrapping = TextWrapping.Wrap
            };
            ProcessColoredText(sb.ToString(), textBlock);

            textTab.Content = new ScrollViewer
            {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            tabControl.Items.Add(textTab);

            
            // Pestaña de Gráficos (solo si hay datos)
            if (btDosesPerFraction != null && btDosesPerFraction.Any())
            {
                var chartTab = new TabItem { Header = "Gráficos" };
                var chartStack = new StackPanel { Orientation = Orientation.Vertical };

                // Gráfico de EQD2 por fracción (solo CTV)
                if (btDosesPerFraction.ContainsKey("HR-CTV"))
                {
                    var ctvDoses = btDosesPerFraction["HR-CTV"];
                    var eqd2PerFxChart = new GroupBox
                    {
                        Header = "EQD2 por Fracción en HR-CTV (α/β=10)",
                        Margin = new Thickness(10),
                        Content = CreateEQD2PerFractionChart(ctvDoses)
                    };
                    chartStack.Children.Add(eqd2PerFxChart);
                }

                // Gráfico de EQD2 total con EBRT
                var totalChart = new GroupBox
                {
                    Header = "EQD2 Total (EBRT + HDR-BT) con Límites Clínicos",
                    Margin = new Thickness(10),
                    Content = CreateEQD2ChartWithLimits(eqd2Total, constraints)
                };
                chartStack.Children.Add(totalChart);

                chartTab.Content = new ScrollViewer
                {
                    Content = chartStack,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                tabControl.Items.Add(chartTab);
            }

            mainGrid.Children.Add(tabControl);

            // Panel de botones (sin cambios)
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10),
                Background = Brushes.Transparent
            };

            var btnTxt = new Button
            {
                Content = "📤 Exportar a TXT",
                Margin = new Thickness(5),
                Style = CreateButtonStyle(Brushes.SteelBlue)
            };
            btnTxt.Click += (s, e) => SaveAsTxt(sb);

            var btnPrint = new Button
            {
                Content = "🖨️ Imprimir Reporte",
                Margin = new Thickness(5),
                Style = CreateButtonStyle(Brushes.DarkSeaGreen)
            };
            btnPrint.Click += (s, e) => PrintReport(sb.ToString());


            var recalcButton = new Button
            {
                Content = "Recalcular Dosis",
                Style = CreateButtonStyle(Brushes.DarkOrange),
                Margin = new Thickness(5),
                ToolTip = "Recalcular dosis para fracciones restantes"
            };
            recalcButton.Click += (s, e) => ShowRecalculationDialog(totalDosisEBRT, totalFraccionesBT);
            buttonPanel.Children.Add(recalcButton);


            buttonPanel.Children.Add(btnTxt);
            buttonPanel.Children.Add(btnPrint);
            Grid.SetRow(buttonPanel, 1);
            mainGrid.Children.Add(buttonPanel);

            window.Content = mainGrid;
            window.ShowDialog();
        }


        //---------------------------++++++++++++++++
        private Canvas CreateEQD2PerFractionChart(List<double> eqd2Values)
        {
            const double margin = 40;
            const double barWidth = 40;
            const double spacing = 20;
            const double chartHeight = 500;
            const double chartWidth = 500;
            const double targetEQD2 = 6.0; // Ajustado a 6 Gy como referencia
            const double tolerance = 0.1;   // ±10% de tolerancia



            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = Brushes.White
            };

            // Configurar escala
            double maxDose = Math.Max(eqd2Values.Max(), targetEQD2 * (1 + tolerance)) * 1.2;
            double scaleFactor = (chartHeight - 2 * margin) / maxDose;

            // Dibujar ejes y líneas de referencia
            // Dentro de DrawAxesWithToleranceLines (o justo antes de llamarlo):
            double axisLengthReductionFactor = 0.5; // Reducir a la mitad (0.5) o ajusta este valor

            // Eje X (horizontal) - Reducir longitud
            var xAxis = new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = margin + (chartWidth * axisLengthReductionFactor), // Longitud reducida
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };

            // Eje Y (vertical) - Reducir altura
            var yAxis = new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = margin,
                Y2 = margin + (chartHeight * axisLengthReductionFactor), // Altura reducida
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };

            DrawAxesWithToleranceLines(canvas, margin, chartHeight, chartWidth, maxDose, scaleFactor, targetEQD2, tolerance);

            // Dibujar barras para cada fracción
            for (int i = 0; i < eqd2Values.Count; i++)
            {
                double xPos = margin + spacing + (i * (barWidth + spacing));
                double eqd2 = eqd2Values[i];
                double barHeight = eqd2 * scaleFactor;

                // Barra con color según tolerancia
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = GetBarColor(eqd2, targetEQD2 * (1 - tolerance), targetEQD2 * (1 + tolerance)),
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(bar, xPos);
                Canvas.SetTop(bar, chartHeight - margin - barHeight);
                canvas.Children.Add(bar);

                // Etiqueta de valor

                // Etiqueta de valor centrada verticalmente en la barra
                var valueLabel = new TextBlock
                {
                    Text = eqd2.ToString("F1") + " Gy",
                    FontSize = 11,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Centrado horizontal
                Canvas.SetLeft(valueLabel, xPos + (barWidth / 2) - 15); // Aproximación para centrar

                // Centrado vertical:
                // 1. Posición superior de la barra: chartHeight - margin - barHeight
                // 2. Añadimos la mitad de la altura de la barra (barHeight/2)
                // 3. Restamos la mitad de la altura estimada del texto (aproximadamente 8px para font-size 10)
                Canvas.SetTop(valueLabel, (chartHeight - margin - barHeight) + (barHeight / 2) - 8);

                //canvas.Children.Add(bar);
                canvas.Children.Add(valueLabel);

                // Etiqueta de fracción
                var fxLabel = new TextBlock
                {
                    Text = $"Fx {i + 1}",
                    FontSize = 11,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(fxLabel, xPos + (barWidth / 2) - 10);
                Canvas.SetTop(fxLabel, chartHeight - margin + 5);
                canvas.Children.Add(fxLabel);
            }

            // Leyenda con los nuevos valores de referencia
            DrawToleranceLegend(canvas, margin, targetEQD2, tolerance);

            return canvas;
        }

        // Método auxiliar para dibujar líneas de referencia con tolerancia
        private void DrawAxesWithToleranceLines(Canvas canvas, double margin, double chartHeight,
                                      double chartWidth, double maxDose, double scaleFactor,
                                      double target, double tolerance)
        {
            // Ejes principales
            var xAxis = new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = chartWidth - margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            canvas.Children.Add(xAxis);

            var yAxis = new Line
            {
                X1 = margin,
                Y1 = margin,
                X2 = margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            canvas.Children.Add(yAxis);

            // Línea de referencia principal (5 Gy)
            double targetY = chartHeight - margin - (target * scaleFactor);
            var targetLine = new Line
            {
                X1 = margin,
                Y1 = targetY,
                X2 = chartWidth - margin,
                Y2 = targetY,
                Stroke = Brushes.Blue,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            };
            canvas.Children.Add(targetLine);

            // Líneas de tolerancia ±10%
            double upperY = chartHeight - margin - (target * (1 + tolerance) * scaleFactor);
            var upperLine = new Line
            {
                X1 = margin,
                Y1 = upperY,
                X2 = chartWidth - margin,
                Y2 = upperY,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 })
            };
            canvas.Children.Add(upperLine);

            double lowerY = chartHeight - margin - (target * (1 - tolerance) * scaleFactor);
            var lowerLine = new Line
            {
                X1 = margin,
                Y1 = lowerY,
                X2 = chartWidth - margin,
                Y2 = lowerY,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 })
            };
            canvas.Children.Add(lowerLine);

            // Etiquetas
            var targetLabel = new TextBlock
            {
                Text = $"{target:F1} Gy (Ref)",
                FontSize = 10,
                Foreground = Brushes.Blue
            };
            Canvas.SetLeft(targetLabel, margin + 10);
            Canvas.SetTop(targetLabel, targetY - 15);
            canvas.Children.Add(targetLabel);

            var upperLabel = new TextBlock
            {
                Text = $"+10% ({target * (1 + tolerance):F1} Gy)",
                FontSize = 9,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(upperLabel, margin + 10);
            Canvas.SetTop(upperLabel, upperY - 15);
            canvas.Children.Add(upperLabel);

            var lowerLabel = new TextBlock
            {
                Text = $"-10% ({target * (1 - tolerance):F1} Gy)",
                FontSize = 9,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(lowerLabel, margin + 10);
            Canvas.SetTop(lowerLabel, lowerY - 15);
            canvas.Children.Add(lowerLabel);

        }

        // Leyenda actualizada para reflejar los nuevos valores
        private void DrawToleranceLegend(Canvas canvas, double margin, double target, double tolerance)
        {
            var legend = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(8),
                Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            };

            // Título
            legend.Children.Add(new TextBlock
            {
                Text = "Leyenda (EQD2 α/β=10)",
                FontWeight = FontWeights.Bold,
                FontSize = 11, // Tamaño ligeramente mayor
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Rangos de tolerancia
            var ranges = new[]
            {
                new { Color = Brushes.Green, Text = $"{target*(1-tolerance):F1}-{target*(1+tolerance):F1} Gy (±10%)" },
                new { Color = Brushes.Orange, Text = $"< {target*(1-tolerance):F1} Gy" },
                new { Color = Brushes.Red, Text = $"> {target*(1+tolerance):F1} Gy" }
            };

            foreach (var range in ranges)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                panel.Children.Add(new Rectangle
                {
                    Width = 20,
                    Height = 20,
                    Fill = range.Color,
                    Stroke = Brushes.Black,
                    Margin = new Thickness(0, 0, 5, 0)
                });
                panel.Children.Add(new TextBlock { Text = range.Text, FontSize = 10 });
                legend.Children.Add(panel);
            }

            // Líneas de referencia
            var refLines = new[]
            {
                new { Color = Brushes.Blue, Text = $"{target:F1} Gy (Referencia)", Style = "dash" },
                new { Color = Brushes.Gray, Text = "Límites ±10%", Style = "dash" }
            };

            foreach (var line in refLines)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                panel.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = 30,
                    Y1 = 8,
                    Y2 = 8,
                    Stroke = line.Color,
                    StrokeThickness = 1.5,
                    StrokeDashArray = line.Style == "dash" ? new DoubleCollection(new[] { 3.0, 3.0 }) : null
                });
                panel.Children.Add(new TextBlock { Text = line.Text, FontSize = 10, Margin = new Thickness(5, 0, 0, 0) });
                legend.Children.Add(panel);
            }

            Canvas.SetLeft(legend, margin + 350);
            Canvas.SetTop(legend, margin + 50);
            canvas.Children.Add(legend);
        }
        //*****************
        //GRÁFICA 2
        private Canvas CreateEQD2ChartWithLimits(Dictionary<string, double> eqd2Total,
           Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            const double margin = 40;
            const double barWidth = 40;
            const double spacing = 20;
            const double chartHeight = 500;
            const double chartWidth = 500;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = Brushes.White
            };

            // Calcular valores máximos para escalado
            double maxValue = Math.Max(
                eqd2Total.Max(x => x.Value),
                constraints.Max(c => c.Value.limitValue)
            );
            maxValue = Math.Ceiling(maxValue * 1.2); // Añadir 20% de margen


            // Eje X (horizontal) - Reducir longitud

            double axisLengthReductionFactor = 0.5; // Reducir a la mitad (0.5) o ajusta este valor

            var xAxis = new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = margin + (chartWidth * axisLengthReductionFactor), // Longitud reducida
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };

            // Eje Y (vertical) - Reducir altura
            var yAxis = new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = margin,
                Y2 = margin + (chartHeight * 0.1), // Altura reducida
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };

            canvas.Children.Add(xAxis);
            canvas.Children.Add(yAxis);

            /*
            // Dibujar marcas en el eje Y
            for (double y = 0; y <= maxValue; y += maxValue / 10)
            {
                double yPos = chartHeight - margin - (y / maxValue * (chartHeight - 2 * margin));

                var tick = new Line
                {
                    X1 = margin - 5,
                    Y1 = yPos,
                    X2 = margin,
                    Y2 = yPos,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                var label = new TextBlock
                {
                    Text = y.ToString("F1"),
                    FontSize = 10,
                    Foreground = Brushes.Black
                };

                Canvas.SetLeft(label, margin - 35);
                Canvas.SetTop(label, yPos - 8);

                canvas.Children.Add(tick);
                canvas.Children.Add(label);
            }
            */

            // Dibujar barras y límites
            double xPos = margin + spacing;
            int index = 0;
            var colors = new[] { Brushes.SteelBlue, Brushes.Green, Brushes.Gold, Brushes.Purple };

            foreach (var item in eqd2Total)
            {
                if (!constraints.ContainsKey(item.Key))
                    continue;

                var constraint = constraints[item.Key];
                double barHeight = (item.Value / maxValue) * (chartHeight - 2 * margin);

                // Dibujar barra de EQD2
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = colors[index % colors.Length],
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                Canvas.SetLeft(bar, xPos);
                Canvas.SetTop(bar, chartHeight - margin - barHeight);

                // Etiqueta de valor
                var valueLabel = new TextBlock
                {
                    Text = item.Value.ToString("F2"),
                    FontSize = 10,
                    Foreground = Brushes.Black
                };

                Canvas.SetLeft(valueLabel, xPos);
                Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 20);

                // Etiqueta de estructura
                var structLabel = new TextBlock
                {
                    Text = item.Key,
                    FontSize = 10,
                    Foreground = Brushes.Black,
                    LayoutTransform = new RotateTransform(-45)
                };

                Canvas.SetLeft(structLabel, xPos);
                Canvas.SetTop(structLabel, chartHeight - margin + 5);

                // Dibujar líneas de límites
                double aimY = chartHeight - margin - (constraint.aimValue / maxValue) * (chartHeight - 2 * margin);
                double limitY = chartHeight - margin - (constraint.limitValue / maxValue) * (chartHeight - 2 * margin);

                var aimLine = new Line
                {
                    X1 = xPos - 5,
                    Y1 = aimY,
                    X2 = xPos + barWidth + 5,
                    Y2 = aimY,
                    Stroke = Brushes.Green,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection(new[] { 2.0, 2.0 })
                };

                var limitLine = new Line
                {
                    X1 = xPos - 5,
                    Y1 = limitY,
                    X2 = xPos + barWidth + 5,
                    Y2 = limitY,
                    Stroke = Brushes.Red,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection(new[] { 2.0, 2.0 })
                };

                // Etiquetas de límites
                var aimLabel = new TextBlock
                {
                    Text = $"Meta: {constraint.aimValue:F1}",
                    FontSize = 8,
                    Foreground = Brushes.Green
                };

                var limitLabel = new TextBlock
                {
                    Text = $"Límite: {constraint.limitValue:F1}",
                    FontSize = 8,
                    Foreground = Brushes.Red
                };

                Canvas.SetLeft(aimLabel, xPos + barWidth + 10);
                Canvas.SetTop(aimLabel, aimY - 10);

                Canvas.SetLeft(limitLabel, xPos + barWidth + 10);
                Canvas.SetTop(limitLabel, limitY - 10);

                // Añadir elementos al canvas
                canvas.Children.Add(bar);
                canvas.Children.Add(valueLabel);
                canvas.Children.Add(structLabel);
                canvas.Children.Add(aimLine);
                canvas.Children.Add(limitLine);
                canvas.Children.Add(aimLabel);
                canvas.Children.Add(limitLabel);

                xPos += barWidth + spacing;
                index++;
            }

            // Leyenda
            var legend = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(margin, 10, 0, 0)
            };

            var legendItems = new[]
            {
                new { Color = Brushes.Green, Text = "Meta Clínica" },
                new { Color = Brushes.Red, Text = "Límite Clínico" }
            };

            foreach (var item in legendItems)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 15, 0) };
                panel.Children.Add(new Rectangle
                {
                    Width = 12,
                    Height = 12,
                    Fill = item.Color,
                    Stroke = Brushes.Black,
                    Margin = new Thickness(0, 0, 5, 0)
                });
                panel.Children.Add(new TextBlock
                {
                    Text = item.Text,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                });
                legend.Children.Add(panel);
            }

            canvas.Children.Add(legend);

            return canvas;
        }

        // Métodos auxiliares para la generación de gráficos
        private Canvas CreateBarChart(Dictionary<string, List<double>> data, bool isTotalEQD2)
        {
            const double margin = 40;
            const double barWidth = 30;
            const double spacing = 10;
            const double chartHeight = 350;
            const double chartWidth = 800;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = Brushes.White
            };

            // Calcular máximo basado en EQD2
            double maxValue = data.Max(kvp => kvp.Value.DefaultIfEmpty().Max());
            maxValue = Math.Ceiling(maxValue * 1.2); // 20% de margen

            // Dibujar ejes
            var xAxis = new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = chartWidth - spacing,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };

            var yAxis = new Line
            {
                X1 = margin,
                Y1 = margin,
                X2 = margin,
                Y2 = chartHeight - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };

            canvas.Children.Add(xAxis);
            canvas.Children.Add(yAxis);

            // Dibujar marcas en el eje Y
            for (double y = 0; y <= maxValue; y += maxValue / 5)
            {
                double yPos = chartHeight - margin - (y / maxValue * (chartHeight - 2 * margin));

                var tick = new Line
                {
                    X1 = margin - 5,
                    Y1 = yPos,
                    X2 = margin,
                    Y2 = yPos,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                var label = new TextBlock
                {
                    Text = y.ToString("F1"),
                    FontSize = 10,
                    Foreground = Brushes.Black
                };

                Canvas.SetLeft(label, margin - 35);
                Canvas.SetTop(label, yPos - 8);

                canvas.Children.Add(tick);
                canvas.Children.Add(label);
            }

            // Dibujar barras
            if (isTotalEQD2)
            {
                var eqd2Data = CalculateTotalEQD2Values(data);
                double xPos = margin + spacing;

                foreach (var item in eqd2Data)
                {
                    double barHeight = (item.Value / maxValue) * (chartHeight - 2 * margin);

                    var bar = new Rectangle
                    {
                        Width = barWidth,
                        Height = barHeight,
                        Fill = GetStructureColor(item.Key),
                        Stroke = Brushes.Black,
                        StrokeThickness = 1
                    };

                    Canvas.SetLeft(bar, xPos);
                    Canvas.SetTop(bar, chartHeight - margin - barHeight);

                    var label = new TextBlock
                    {
                        Text = item.Key,
                        FontSize = 10,
                        Foreground = Brushes.Black,
                        LayoutTransform = new RotateTransform(-45)
                    };

                    Canvas.SetLeft(label, xPos);
                    Canvas.SetTop(label, chartHeight - margin + 5);

                    // Etiqueta de valor centrada verticalmente en la barra
                    var valueLabel = new TextBlock
                    {
                        Text = item.Value.ToString("F2") + " Gy",
                        FontSize = 10,
                        Foreground = Brushes.Black,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    // Centrado horizontal
                    Canvas.SetLeft(valueLabel, xPos + (barWidth / 2) - 15); // Aproximación para centrar

                    // Centrado vertical:
                    // 1. Posición superior de la barra: chartHeight - margin - barHeight
                    // 2. Añadimos la mitad de la altura de la barra (barHeight/2)
                    // 3. Restamos la mitad de la altura estimada del texto (aproximadamente 8px para font-size 10)
                    Canvas.SetTop(valueLabel, (chartHeight - margin - barHeight) + (barHeight / 2) - 8);

                    canvas.Children.Add(valueLabel);
                    //xPos += barWidth + spacing;
                }
            }
            else
            {
                // Gráfico por fracciones
                int maxFractions = data.Max(kvp => kvp.Value.Count);
                double groupWidth = (barWidth + spacing) * data.Count;
                double xStart = margin + spacing;

                for (int i = 0; i < maxFractions; i++)
                {
                    double xPos = xStart + i * (groupWidth + spacing * 3);
                    int colorIndex = 0;

                    foreach (var kvp in data)
                    {
                        if (i < kvp.Value.Count)
                        {
                            double value = kvp.Value[i];
                            double barHeight = (value / maxValue) * (chartHeight - 2 * margin);

                            var bar = new Rectangle
                            {
                                Width = barWidth,
                                Height = barHeight,
                                Fill = GetStructureColor(kvp.Key),
                                Stroke = Brushes.Black,
                                StrokeThickness = 1
                            };

                            Canvas.SetLeft(bar, xPos);
                            Canvas.SetTop(bar, chartHeight - margin - barHeight);

                            // Etiqueta de valor
                            if (barHeight > 15) // Solo mostrar si hay espacio
                            {
                                var valueLabel = new TextBlock
                                {
                                    Text = value.ToString("F2") + "Gy",
                                    FontSize = 8,
                                    Foreground = Brushes.Black
                                };

                                Canvas.SetLeft(valueLabel, xPos);
                                Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 15);
                                canvas.Children.Add(valueLabel);
                            }

                            // Etiqueta de fracción (solo en primera estructura)
                            if (colorIndex == 0)
                            {
                                var fracLabel = new TextBlock
                                {
                                    Text = $"Fx {i + 1}",
                                    FontSize = 10,
                                    Foreground = Brushes.Black
                                };

                                Canvas.SetLeft(fracLabel, xPos + (barWidth * data.Count / 2) - 10);
                                Canvas.SetTop(fracLabel, chartHeight - margin + 5);
                                canvas.Children.Add(fracLabel);
                            }

                            canvas.Children.Add(bar);
                            xPos += barWidth + spacing;
                            colorIndex++;
                        }
                    }
                }
            }

            // Añadir línea de referencia a 5 Gy EQD2
            double targetY = chartHeight - margin - (5.0 / maxValue * (chartHeight - 2 * margin));
            var targetLine = new Line
            {
                X1 = margin,
                Y1 = targetY,
                X2 = chartWidth - margin,
                Y2 = targetY,
                Stroke = Brushes.Green,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 2.0, 2.0 })
            };
            canvas.Children.Add(targetLine);

            var targetLabel = new TextBlock
            {
                Text = "Objetivo: 5.00 Gy EQD2",
                FontSize = 10,
                Foreground = Brushes.Green
            };
            Canvas.SetLeft(targetLabel, margin + 10);
            Canvas.SetTop(targetLabel, targetY - 20);
            canvas.Children.Add(targetLabel);

            return canvas;
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos auxiliares
        //----------------------------------------------------------------------------------------------------------------------
        private Dictionary<string, double> CalculateTotalEQD2Values(Dictionary<string, List<double>> btDosesPerFraction)
        {
            var result = new Dictionary<string, double>();
            var alphaBetaValues = new Dictionary<string, double>
            {
                {"HR-CTV", alphaBetaTumor},
                {"Recto-HDR", alphaBetaOAR},
                {"Vejiga-HDR", alphaBetaOAR},
                {"Sigma-HDR", alphaBetaOAR}
            };

            foreach (var kvp in btDosesPerFraction)
            {
                double totalEQD2 = 0;
                foreach (var dose in kvp.Value)
                {
                    double bed = CalculateBEDWithTimeAdjustment(
                        dose,
                        1, // Por fracción
                        alphaBetaValues[kvp.Key],
                        totalTime,
                        Tdelay,
                        k);

                    totalEQD2 += CalculateEQD2(bed, alphaBetaValues[kvp.Key]);
                }

                result.Add(kvp.Key, totalEQD2);
            }

            return result;
        }

        private Brush GetStructureColor(string structureId)
        {
            switch (structureId)
            {
                case "HR-CTV":
                    return new SolidColorBrush(Color.FromRgb(70, 130, 180)); // SteelBlue
                case "Recto-HDR":
                    return new SolidColorBrush(Color.FromRgb(50, 205, 50)); // LimeGreen
                case "Vejiga-HDR":
                    return new SolidColorBrush(Color.FromRgb(255, 215, 0)); // Gold
                case "Sigma-HDR":
                    return new SolidColorBrush(Color.FromRgb(128, 0, 128)); // Púrpura
                default:
                    return Brushes.Gray;
            }
        }

        public RecalculatedDoseResults RecalculateRemainingFractions(
            double prescribedTotalDose,
            double originalDosePerFraction,
            int totalPlannedFractions,
            int deliveredFractions,
            DateTime treatmentStartDate,
            DateTime originalEndDate,
            DateTime newEndDate,
            double alphaBetaTumor = 10.0,
            double alphaBetaOAR = 3.0,
            double k = 0.7,
            double Tdel = 28.0)
        {
            // Validación de parámetros
            if (deliveredFractions < 0)
                throw new ArgumentException("Las fracciones entregadas no pueden ser negativas");

            if (deliveredFractions >= totalPlannedFractions)
                throw new ArgumentException("Las fracciones entregadas deben ser menores que las planificadas");

            var results = new RecalculatedDoseResults();
            results.NewFractions = totalPlannedFractions - deliveredFractions;

            // Cálculo de tiempos
            double originalDuration = (originalEndDate - treatmentStartDate).TotalDays;
            double newDuration = (newEndDate - treatmentStartDate).TotalDays;

            // Cálculo de BED necesario
            double originalBED = CalculateBEDWithTimeAdjustment(
                originalDosePerFraction,
                totalPlannedFractions,
                alphaBetaTumor,
                originalDuration,
                Tdel,
                k);

            double deliveredBED = CalculateBED(originalDosePerFraction, deliveredFractions, alphaBetaTumor);
            double remainingBEDNeeded = originalBED - deliveredBED;

            // Ajuste por tiempo adicional
            if (newDuration > Tdel)
            {
                remainingBEDNeeded += k * (newDuration - Tdel);
            }

            // Solución cuadrática para nueva dosis por fracción
            double a = results.NewFractions / alphaBetaTumor;
            double b = results.NewFractions;
            double c = -remainingBEDNeeded;

            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
                throw new InvalidOperationException("No se puede alcanzar la dosis necesaria con los parámetros dados");

            results.NewDosePerFraction = (-b + Math.Sqrt(discriminant)) / (2 * a);

            // Cálculos finales
            results.TotalBED = deliveredBED + CalculateBED(results.NewDosePerFraction, results.NewFractions, alphaBetaTumor);
            if (newDuration > Tdel)
            {
                results.TotalBED -= k * (newDuration - Tdel);
            }

            results.TotalEQD2 = CalculateEQD2(results.TotalBED, alphaBetaTumor);
            results.OAR_BED = CalculateBED(originalDosePerFraction, deliveredFractions, alphaBetaOAR) +
                             CalculateBED(results.NewDosePerFraction, results.NewFractions, alphaBetaOAR);
            results.OAR_EQD2 = CalculateEQD2(results.OAR_BED, alphaBetaOAR);

            return results;
        }

        // Método para mostrar el diálogo de recálculo
        private void ShowRecalculationDialog( double totalDosisEBRT, int totalFraccionesBT )
        {
            // Crear ventana de diálogo
            var dialog = new Window
            {
                Title = "Recálculo de Fracciones Restantes",
                Width = 400,
                Height = 450,  // Aumentada para acomodar el nuevo campo
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            // Panel principal
            var stackPanel = new StackPanel { Margin = new Thickness(15) };

            // Campo para fecha de inicio del tratamiento
            var startDatePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            startDatePanel.Children.Add(new Label { Content = "Fecha inicio tratamiento (dd/MM/yyyy):", Width = 150 });
            var datePicker = new DatePicker
            {
                Width = 120,
                SelectedDate = DateTime.Today  // Valor por defecto
            };
            startDatePanel.Children.Add(datePicker);
            stackPanel.Children.Add(startDatePanel);

            // Campo para fracciones restantes
            var fractionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            fractionsPanel.Children.Add(new Label { Content = "Fracciones restantes:", Width = 150 });
            var txtFractions = new TextBox { Width = 50, Text = "5" };
            fractionsPanel.Children.Add(txtFractions);
            stackPanel.Children.Add(fractionsPanel);

            // Campo para días adicionales
            var daysPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
            daysPanel.Children.Add(new Label { Content = "Días adicionales:", Width = 150 });
            var txtDays = new TextBox { Width = 50, Text = "7" };
            daysPanel.Children.Add(txtDays);
            stackPanel.Children.Add(daysPanel);



            // Mostrar información del plan actual
            double newtotalDosisBT = 86 - totalDosisEBRT;
            totalFraccionesBT = 5;

            var currentPlanInfo = new TextBlock
            {
                Text = $"Plan actual:\n- Dosis total: {newtotalDosisBT:F1} Gy\n- Fracciones totales: {totalFraccionesBT}",
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(currentPlanInfo);

            // Botón de cálculo
            var btnCalculate = new Button
            {
                Content = "Calcular",
                Width = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = CreateButtonStyle(Brushes.SteelBlue)
            };

            // Área de resultados
            var resultText = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas")
            };

            btnCalculate.Click += (s, e) =>
            {
                try
                {
                    // Validar y obtener valores de entrada
                    if (datePicker.SelectedDate == null)
                    {
                        throw new Exception("Debe seleccionar una fecha de inicio");
                    }

                    DateTime treatmentStartDate = datePicker.SelectedDate.Value;
                    int remainingFractions = int.Parse(txtFractions.Text);
                    int additionalDays = int.Parse(txtDays.Text);

                    // Calcular fechas
                    DateTime originalEndDate = treatmentStartDate.AddDays(56); // 8 semanas = 56 días
                    DateTime newEndDate = originalEndDate.AddDays(additionalDays);

                    // Calcular fracciones entregadas
                    int deliveredFractions = totalFraccionesBT - remainingFractions;

                    if (deliveredFractions < 0)
                    {
                        throw new Exception("Las fracciones restantes no pueden ser mayores que las totales");
                    }

                    // Ejecutar recálculo
                    var results = RecalculateRemainingFractions(
                        prescribedTotalDose: newtotalDosisBT,
                        originalDosePerFraction: newtotalDosisBT / totalFraccionesBT,
                        totalPlannedFractions: totalFraccionesBT,
                        deliveredFractions: deliveredFractions,
                        treatmentStartDate: treatmentStartDate,
                        originalEndDate: originalEndDate,
                        newEndDate: newEndDate,
                        alphaBetaTumor: alphaBetaTumor,
                        alphaBetaOAR: alphaBetaOAR,
                        k: k,
                        Tdel: Tdelay);

                    // Mostrar resultados
                    resultText.Text = $"Fecha inicio: {treatmentStartDate:dd/MM/yyyy}\n" +
                                    $"Nueva dosis/fracción: {results.NewDosePerFraction:F2} Gy\n" +
                                    $"Fracciones restantes: {results.NewFractions}\n" +
                                    $"Dosis total restante: {results.TotalDose:F2} Gy\n" +
                                    $"EQD2 Tumor: {results.TotalEQD2:F2} Gy\n" +
                                    $"EQD2 OAR: {results.OAR_EQD2:F2} Gy\n" +
                                    $"Nueva fecha final: {newEndDate:dd/MM/yyyy}";
                }
                catch (Exception ex)
                {
                    resultText.Text = $"Error: {ex.Message}";
                }
            };

            stackPanel.Children.Add(btnCalculate);
            stackPanel.Children.Add(resultText);
            dialog.Content = stackPanel;
            dialog.ShowDialog();
        }
        private string _selectedPTV = "PTV_56"; // Valor por defecto

        // Agregar en las constantes
        private const double DosePTV56 = 56.0;
        private const double DosePTV50_4 = 50.4;

        public class PTVSelectionDialog : Window
        {
            public string SelectedPTV { get; private set; } = "PTV_56";

            public PTVSelectionDialog()
            {
                Title = "Seleccionar PTV Objetivo";
                Width = 300;
                Height = 200;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;

                var stackPanel = new StackPanel { Margin = new Thickness(15) };

                var label = new Label { Content = "Seleccione la estructura objetivo:", Margin = new Thickness(0, 0, 0, 10) };

                var radioPTV56 = new RadioButton { Content = "PTV_56 (56 Gy)", IsChecked = true, Margin = new Thickness(5) };
                var radioPTV50_4 = new RadioButton { Content = "PTV_50.4 (50.4 Gy)", Margin = new Thickness(5) };

                var button = new Button
                {
                    Content = "Aceptar",
                    Width = 100,
                    Margin = new Thickness(0, 20, 0, 0)
                };

                radioPTV56.Checked += (s, e) => SelectedPTV = "PTV_56";
                radioPTV50_4.Checked += (s, e) => SelectedPTV = "PTV_50.4";
                button.Click += (s, e) => DialogResult = true;

                stackPanel.Children.Add(label);
                stackPanel.Children.Add(radioPTV56);
                stackPanel.Children.Add(radioPTV50_4);
                stackPanel.Children.Add(button);

                Content = stackPanel;
            }
        }
    }
    public class RecalculatedDoseResults
    {
        public int NewFractions { get; set; }
        public double NewDosePerFraction { get; set; }
        public double TotalDose => NewFractions * NewDosePerFraction;
        public double TotalBED { get; set; }
        public double TotalEQD2 { get; set; }
        public double OAR_BED { get; set; }
        public double OAR_EQD2 { get; set; }

        public override string ToString()
        {
            return $"Fracciones restantes: {NewFractions}\n" +
                   $"Nueva dosis/fracción: {NewDosePerFraction:F2} Gy\n" +
                   $"Dosis total restante: {TotalDose:F2} Gy\n" +
                   $"BED Tumor: {TotalBED:F2} Gy₁₀\n" +
                   $"EQD2 Tumor: {TotalEQD2:F2} Gy\n" +
                   $"BED OAR: {OAR_BED:F2} Gy₃\n" +
                   $"EQD2 OAR: {OAR_EQD2:F2} Gy";
        }
    }
}
