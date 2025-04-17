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

            // Inicialización de variables
            StringBuilder sb = new StringBuilder();
            string patientName = context.Patient.Name;
            string patientId = context.Patient.Id;
            double totalDosisEBRT = 0, totalDosisBT = 0;
            int totalFraccionesEBRT = 0, totalFraccionesBT = 0;

            // Diccionario para acumular EQD2 total
            Dictionary<string, double> eqd2Total = new Dictionary<string, double>
            {
                {"PTV+CTV", 0}, {"Recto", 0}, {"Vejiga", 0}, {"Sigma", 0}
            };

            // Restricciones clínicas
            var constraints = new Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)>
            {
                { "Recto",    (65.0, 75.0, "lessThan", "< 65 Gy", "< 75 Gy") },
                { "Vejiga",   (80.0, 90.0, "lessThan", "< 80 Gy", "< 90 Gy") },
                { "Sigma",    (70.0, 75.0, "lessThan", "< 70 Gy", "< 75 Gy") },
                { "PTV+CTV",  (85.0, 95.0, "range",    "> 85 Gy", "< 95 Gy") }
            };

            //----------------------------------------------------------------------------------------------------------------------
            // Encabezado del reporte
            //----------------------------------------------------------------------------------------------------------------------
            GenerateReportHeader(sb, patientName, patientId);

            //----------------------------------------------------------------------------------------------------------------------
            // Procesamiento de cursos (EBRT y BT)
            //----------------------------------------------------------------------------------------------------------------------
            Dictionary<string, List<double>> btDosesData = null;

            foreach (Course course in context.Patient.Courses)
            {
                if (IsEBRTCourse(course.Id))
                {
                    ProcessEBRTCourse(course, sb, ref totalDosisEBRT, ref totalFraccionesEBRT, eqd2Total);
                }
                else if (IsBrachyCourse(course.Id))
                {
                    btDosesData = ProcessBrachyCourse(course, sb, ref totalDosisBT, ref totalFraccionesBT, eqd2Total);
                }
            }

            //----------------------------------------------------------------------------------------------------------------------
            // Sección Total con comparación de Aims y Límites
            //----------------------------------------------------------------------------------------------------------------------
            GenerateTotalSection(sb, eqd2Total, constraints);

            //----------------------------------------------------------------------------------------------------------------------
            // Evaluación final del plan
            //----------------------------------------------------------------------------------------------------------------------
            EvaluateTreatmentPlan(sb, eqd2Total, constraints);

            //----------------------------------------------------------------------------------------------------------------------
            // Mostrar la ventana con los gráficos
            //----------------------------------------------------------------------------------------------------------------------
            ShowReportWindow(sb, btDosesData, eqd2Total, constraints);
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos auxiliares para procesamiento de cursos
        //----------------------------------------------------------------------------------------------------------------------
        private bool IsEBRTCourse(string courseId)
        {
            return courseId.Contains("Cervix") || courseId.Contains("EBRT") || courseId.Contains("CERVIX") || courseId.Contains("Cérvix");
        }

        private bool IsBrachyCourse(string courseId)
        {
            return courseId.Contains("Braqui") || courseId.Contains("Fletcher") || courseId.Contains("FLETCHER") || courseId.Contains("HDR");
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Procesamiento de EBRT
        //----------------------------------------------------------------------------------------------------------------------
        private void ProcessEBRTCourse(Course course, StringBuilder sb, ref double totalDosisEBRT, ref int totalFraccionesEBRT, Dictionary<string, double> eqd2Total)
        {
            // Obtener esquema de tratamiento
            string treatmentScheme = GetTreatmentScheme(course);
            sb.AppendLine("\n========================== SECCIÓN EBRT ==========================");
            sb.AppendLine($"║   Esquema: {treatmentScheme}   ║");
            sb.AppendLine("------------------------------------------------------------------");

            // Buscar el primer plan aprobado con 28 fracciones
            var plan28Fx = course.ExternalPlanSetups
                .FirstOrDefault(p => IsPlanApproved(p) && p.NumberOfFractions == 28);

            if (plan28Fx == null)
            {
                sb.AppendLine("⚠ No se encontró plan aprobado con 28 fracciones");
                return;
            }

            totalFraccionesEBRT = plan28Fx.NumberOfFractions ?? 0;
            totalDosisEBRT = plan28Fx.TotalDose.Dose;

            sb.AppendLine($"\nPlan seleccionado: {plan28Fx.Id}");
            sb.AppendLine("----------------------------------------------");
            sb.AppendLine("| Estructura  | Dosis[Gy]    | EQD2 [Gy]      |");
            sb.AppendLine("-----------------------------------------------");

            var structures = new[] {
                ("PTV_56", targetVolumeRel90, alphaBetaTumor, "PTV+CTV"),
                ("Recto", targetVolumeAbs2, alphaBetaOAR, "Recto"),
                ("Vejiga", targetVolumeAbs2, alphaBetaOAR, "Vejiga"),
                ("Sigma", targetVolumeAbs2, alphaBetaOAR, "Sigma")
            };

            foreach (var (structureId, volume, alphaBeta, key) in structures)
            {
                double doseAtVolume = volume == targetVolumeAbs2 ?
                    GetDoseAtVolumeAbsoluta(plan28Fx, structureId, volume) :
                    GetDoseAtVolume(plan28Fx, structureId, volume);

                if (double.IsNaN(doseAtVolume) || doseAtVolume <= 0)
                    continue;

                double dosePerFraction = doseAtVolume / 100.0 / totalFraccionesEBRT;
                double bed = CalculateBED(dosePerFraction, totalFraccionesEBRT, alphaBeta);
                double eqd2 = CalculateEQD2(bed, alphaBeta);

                sb.AppendLine($"| {structureId,-10} | {doseAtVolume / 100,-7:F2} | {eqd2,-7:F2} |");
                eqd2Total[key] += eqd2;

                if (key == "PTV+CTV")
                {
                    totalDosisEBRT = eqd2;
                }
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Procesamiento de Braquiterapia
        //----------------------------------------------------------------------------------------------------------------------
        private Dictionary<string, List<double>> ProcessBrachyCourse(Course course, StringBuilder sb, ref double totalDosisBT, ref int totalFraccionesBT, Dictionary<string, double> eqd2Total)
        {
            var plans = course.BrachyPlanSetups.OrderBy(p => p.Id).ToList();
            if (!plans.Any()) return null;

            string treatmentScheme = GetTreatmentScheme(course);

            // Formato idéntico al V40
            sb.AppendLine("\n====================== SECCIÓN HDR-BT ======================");
            sb.AppendLine($"║   Esquema de tratamiento: {treatmentScheme}   ║");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       | Métrica       | Fx #1    | Fx #2    | Fx #3    | Fx #4    | Fx #5    | Total     |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            var structures = new[] {
        ("HR-CTV", targetVolumeRel90, alphaBetaTumor, "PTV+CTV", "D90% [Gy]"),
        ("Recto-HDR", targetVolumeAbs2, alphaBetaOAR, "Recto", "D2cc [Gy]"),
        ("Vejiga-HDR", targetVolumeAbs2, alphaBetaOAR, "Vejiga", "D2cc [Gy]"),
        ("Sigma-HDR", targetVolumeAbs2, alphaBetaOAR, "Sigma", "D2cc [Gy]")
    };

            Dictionary<string, List<double>> btDosesPerFraction = new Dictionary<string, List<double>>();
            foreach (var s in structures) btDosesPerFraction.Add(s.Item1, new List<double>());

            foreach (var (structureId, defaultVolume, alphaBeta, key, metric) in structures)
            {
                sb.Append($"| {structureId,-15} | {metric,-12} |");
                List<double> fractionDoses = new List<double>();
                double totalDose = 0;

                foreach (var plan in plans)
                {
                    double volumeToUse = structureId == "HR-CTV" ?
                        plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId)?.Volume * 0.9 ?? defaultVolume :
                        defaultVolume;

                    double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, volumeToUse) / 100.0;
                    fractionDoses.Add(doseAtVolume);
                    totalDose += doseAtVolume;
                    sb.Append($" {doseAtVolume,-7:F2} |");
                }
                sb.Append($" {totalDose,-7:F2} |");
                sb.AppendLine();

                btDosesPerFraction[structureId] = fractionDoses;

                // Línea EQD2 (se mantiene igual que en V40)
                sb.Append($"| {"",-15} | {"EQD2 [Gy]",-12} |");
                double totalEQD2 = 0;

                foreach (var plan in plans)
                {
                    double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, defaultVolume);
                    double dosePerFraction = doseAtVolume / 100.0 / (double)plan.NumberOfFractions;
                    double bed = CalculateBEDWithTimeAdjustment(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta, totalTime, Tdelay, k);
                    double eqd2 = CalculateEQD2(bed, alphaBeta);

                    totalEQD2 += eqd2;
                    sb.Append($" {eqd2,-7:F2} |");
                }
                sb.Append($" {totalEQD2,-7:F2} |");
                sb.AppendLine();
                sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

                eqd2Total[key] += totalEQD2;
            }
            return btDosesPerFraction;
        }

        //**********************
        private Canvas CreateCombinedChart(Dictionary<string, List<double>> btEQD2PerFraction,
                                 Dictionary<string, double> eqd2Total,
                                 Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
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
        //*******
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

            // Línea de referencia principal (8 Gy)
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
        //***********

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
            sb.AppendLine("Resumen Consolidado de Datos EQD2, Ajuste por Tiempo y Evaluación");
            sb.AppendLine("=====================================================================================");
            sb.AppendLine($" Paciente: {patientName}");
            sb.AppendLine($" ID: {patientId}");
            sb.AppendLine($" α/β Tumor: {alphaBetaTumor}   |   α/β OAR: {alphaBetaOAR}");
            sb.AppendLine("-------------------------------------------------------------------------------------");
        }

        private void GenerateTotalSection(StringBuilder sb, Dictionary<string, double> eqd2Total,
            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            sb.AppendLine("\n====================== SECCIÓN TOTAL EBRT + HDR-BT ======================");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       | EQD2 Total (Gy)  | Meta         | Límite       | Concepto Final                     |");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");

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
                var run = new Run(line + Environment.NewLine)
                {
                    Foreground = Brushes.Black // Color base para todo el texto
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

        //**********************---------------------*****
        private void ShowReportWindow(StringBuilder sb,
                            Dictionary<string, List<double>> btEQD2PerFraction,
                            Dictionary<string, double> eqd2Total,
                            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            var window = new Window
            {
                Title = "Resumen Dosimétrico - V42",
                Width = 1200,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Solo una pestaña con el reporte completo
            var tabControl = new TabControl();
            var reportTab = new TabItem { Header = "Reporte" };

            // Contenedor principal con scroll
            var scrollViewer = new ScrollViewer();
            var mainStack = new StackPanel();

            // 1. Parte de texto del reporte
            var textBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(15),
                TextWrapping = TextWrapping.Wrap,
                Text = sb.ToString()
            };
            mainStack.Children.Add(textBlock);

            // 2. Gráficos (si hay datos)
            if (btEQD2PerFraction != null && btEQD2PerFraction.Any())
            {
                // Gráfico combinado
                var chartContainer = new GroupBox
                {
                    Header = "Resumen Gráfico",
                    Margin = new Thickness(15),
                    Content = CreateCombinedChart(btEQD2PerFraction, eqd2Total, constraints)
                };
                mainStack.Children.Add(chartContainer);
            }

            scrollViewer.Content = mainStack;
            reportTab.Content = scrollViewer;
            tabControl.Items.Add(reportTab);

            // Panel de botones
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };

            var btnTxt = new Button
            {
                Content = "📤 Exportar a TXT",
                Style = CreateButtonStyle(Brushes.SteelBlue),
                Margin = new Thickness(5)
            };
            btnTxt.Click += (s, e) => SaveAsTxt(sb);

            var btnPrint = new Button
            {
                Content = "🖨️ Imprimir Reporte",
                Style = CreateButtonStyle(Brushes.DarkSeaGreen),
                Margin = new Thickness(5)
            };
            btnPrint.Click += (s, e) => PrintReport(sb.ToString());

            buttonPanel.Children.Add(btnTxt);
            buttonPanel.Children.Add(btnPrint);

            // Ensamblar la ventana
            mainGrid.Children.Add(tabControl);
            Grid.SetRow(buttonPanel, 1);
            mainGrid.Children.Add(buttonPanel);

            window.Content = mainGrid;
            window.ShowDialog();
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos de generación de gráficos y reporte
        //----------------------------------------------------------------------------------------------------------------------


        //---------------------------++++++++++++++++
        private Canvas CreateEQD2PerFractionChart(List<double> eqd2Values)
        {
            const double margin = 50;
            const double barWidth = 50;
            const double spacing = 30;
            const double chartHeight = 500;
            const double chartWidth = 900;
            const double targetEQD2 = 5.0;
            const double limitEQD2 = 8.0; // Nuevo límite de 8 Gy

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = Brushes.White
            };

            // Configurar escala
            double maxDose = Math.Max(eqd2Values.Max(), limitEQD2) * 1.2;
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
                var valueLabel = new TextBlock
                {
                    Text = eqd2.ToString("F1") + " Gy",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(valueLabel, xPos + (barWidth / 2) - 15);
                Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 25);
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

            // Leyenda mejorada
            DrawLegend(canvas, margin, targetEQD2, limitEQD2);

            return canvas;
        }
        //-------------------------------r
        private void DrawAxesWithLimits(Canvas canvas, double margin, double chartHeight,
                              double chartWidth, double maxDose, double scaleFactor,
                              double target, double limit)
        {
            // Eje X
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

            // Eje Y
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

            // Marcas y valores del eje Y
            for (double dose = 0; dose <= maxDose; dose += 2)
            {
                double yPos = chartHeight - margin - (dose * scaleFactor);

                var tick = new Line
                {
                    X1 = margin - 5,
                    Y1 = yPos,
                    X2 = margin,
                    Y2 = yPos,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                canvas.Children.Add(tick);

                var label = new TextBlock
                {
                    Text = dose.ToString("F1"),
                    FontSize = 10,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(label, margin - 30);
                Canvas.SetTop(label, yPos - 10);
                canvas.Children.Add(label);
            }

            // Línea de objetivo (5 Gy)
            double targetY = chartHeight - margin - (target * scaleFactor);
            var targetLine = new Line
            {
                X1 = margin,
                Y1 = targetY,
                X2 = chartWidth - margin,
                Y2 = targetY,
                Stroke = Brushes.Green,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            };
            canvas.Children.Add(targetLine);

            // Línea de límite (8 Gy)
            double limitY = chartHeight - margin - (limit * scaleFactor);
            var limitLine = new Line
            {
                X1 = margin,
                Y1 = limitY,
                X2 = chartWidth - margin,
                Y2 = limitY,
                Stroke = Brushes.Red,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 })
            };
            canvas.Children.Add(limitLine);
        }

        private class LegendItem
        {
            public Brush Color { get; set; }
            public string Text { get; set; }
            public bool IsLine { get; set; }
        }

        private void DrawLegend(Canvas canvas, double margin, double target, double limit)
        {
            var legend = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
            };

            // Usamos la clase LegendItem en lugar de tipos anónimos
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
        //*****************
        private Canvas CreateEQD2ChartWithLimits(Dictionary<string, double> eqd2Total,
           Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            const double margin = 40;
            const double barWidth = 30;
            const double spacing = 20;
            const double chartHeight = 400;
            const double chartWidth = 600;

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

            // Dibujar ejes
            var xAxis = new Line
            {
                X1 = margin,
                Y1 = chartHeight - margin,
                X2 = chartWidth - margin,
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
        //////////----------+++++++++++++++++++++++++++++++++++++

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

                    var valueLabel = new TextBlock
                    {
                        Text = item.Value.ToString("F2"),
                        FontSize = 10,
                        Foreground = Brushes.Black
                    };

                    Canvas.SetLeft(valueLabel, xPos);
                    Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 20);

                    canvas.Children.Add(bar);
                    canvas.Children.Add(label);
                    canvas.Children.Add(valueLabel);

                    xPos += barWidth + spacing;
                }
            }
            else
            {
                // Gráfico por fracciones
                // Dibujar barras para EQD2 por fracción
                int maxFractions = data.Max(kvp => kvp.Value.Count);
                double groupWidth = (barWidth + spacing) * data.Count;
                double xStart = margin + spacing;

                for (int i = 0; i < maxFractions; i++)
                {
                    double xPos = xStart + i * (groupWidth + spacing * 3);

                    // Solo mostrar HR-CTV como solicitado
                    if (data.ContainsKey("HR-CTV") && i < data["HR-CTV"].Count)
                    {
                        double value = data["HR-CTV"][i];
                        double barHeight = (value / maxValue) * (chartHeight - 2 * margin);

                        var bar = new Rectangle
                        {
                            Width = barWidth,
                            Height = barHeight,
                            Fill = GetStructureColor("HR-CTV"),
                            Stroke = Brushes.Black,
                            StrokeThickness = 1
                        };

                        Canvas.SetLeft(bar, xPos);
                        Canvas.SetTop(bar, chartHeight - margin - barHeight);

                        // Etiqueta de valor
                        var valueLabel = new TextBlock
                        {
                            Text = value.ToString("F2"),
                            FontSize = 10,
                            Foreground = Brushes.Black
                        };
                        Canvas.SetLeft(valueLabel, xPos);
                        Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 20);
                        canvas.Children.Add(valueLabel);

                        // Etiqueta de fracción
                        var fracLabel = new TextBlock
                        {
                            Text = $"Fx {i + 1}",
                            FontSize = 10,
                            Foreground = Brushes.Black
                        };
                        Canvas.SetLeft(fracLabel, xPos + (barWidth / 2) - 10);
                        Canvas.SetTop(fracLabel, chartHeight - margin + 5);
                        canvas.Children.Add(fracLabel);
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
    }
}
