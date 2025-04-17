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

[assembly: AssemblyVersion("1.0.0.9")]
[assembly: AssemblyFileVersion("1.0.0.9")]
[assembly: AssemblyInformationalVersion("36.0")]

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
            ShowReportWindow(sb, btDosesData);
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

            // Obtener esquema de tratamiento
            string treatmentScheme = GetTreatmentScheme(course);

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

            Dictionary<string, List<double>> btDosesPerFraction = new Dictionary<string, List<double>>
            {
                {"HR-CTV", new List<double>()},
                {"Recto-HDR", new List<double>()},
                {"Vejiga-HDR", new List<double>()},
                {"Sigma-HDR", new List<double>()}
            };


            //////////////////////////////////////////////////////////////////////////

            foreach (var (structureId, defaultVolume, alphaBeta, key, metric) in structures)
            {
                sb.Append($"| {structureId,-15} | {metric,-12} |");
                List<double> fractionDoses = new List<double>();
                double totalDose = 0;

                foreach (var plan in plans)
                {
                    double volumeToUse = defaultVolume;
                    if (structureId == "HR-CTV")
                    {
                        var estructura = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
                        if (estructura != null)
                            volumeToUse = estructura.Volume * 0.9;
                    }

                    double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, volumeToUse) / 100.0;
                    fractionDoses.Add(doseAtVolume);
                    totalDose += doseAtVolume;
                    sb.Append($" {doseAtVolume,-7:F2} |");
                }
                sb.Append($" {totalDose,-7:F2} |");
                sb.AppendLine();

                btDosesPerFraction[structureId] = fractionDoses;

                // Línea para valores EQD2
                sb.Append($"| {"",-15} | {"EQD2 [Gy]",-12} |");
                double totalEQD2 = 0;

                foreach (var plan in plans)
                {
                    double volumeToUse = defaultVolume;
                    if (structureId == "HR-CTV")
                    {
                        var estructura = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
                        if (estructura != null)
                            volumeToUse = estructura.Volume * 0.9;
                    }

                    double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, volumeToUse);
                    double dosePerFraction = doseAtVolume / 100.0 / (double)plan.NumberOfFractions;
                    double bed = CalculateBEDWithTimeAdjustment(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta, totalTime, Tdelay, k);
                    double eqd2 = CalculateEQD2(bed, alphaBeta);

                    totalEQD2 += eqd2;
                    sb.Append($" {eqd2,-7:F2} |");
                }
                sb.Append($" {totalEQD2,-7:F2} |");
                sb.AppendLine();

                eqd2Total[key] += totalEQD2;

                // Línea separadora
                sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            }
            return btDosesPerFraction;
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
                    double totalDose = firstPlan.DosePerFraction.Dose*fractions;
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

                    return $"{totalDose*5:F1} cGy en {fractions*5} fx";
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
            sb.AppendLine(" PRUEBA V36 Resumen Consolidado de Datos EQD2, Ajuste por Tiempo y Evaluación");
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
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Archivo de texto|*.txt",
                Title = "Guardar reporte como TXT",
                FileName = $"Reporte_Dosimetria_{DateTime.Now:yyyyMMdd}.txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveDialog.FileName, sb.ToString());
                MessageBox.Show("Reporte guardado como TXT exitosamente.", "Éxito",
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        //----------------------------------------------------------------------------------------------------------------------
        // Métodos de generación de gráficos y reporte
        //----------------------------------------------------------------------------------------------------------------------
        private void ShowReportWindow(StringBuilder sb, Dictionary<string, List<double>> btDosesPerFraction = null)
        {
            var window = new Window
            {
                Title = "Resumen Dosimétrico - V36",
                Width = 1200,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Panel principal con pestañas
            var tabControl = new TabControl();

            // Pestaña 1: Reporte de texto
            var textTab = new TabItem { Header = "Reporte" };
            var textBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(15),
                TextWrapping = TextWrapping.Wrap
            };
            ProcessColoredText(sb.ToString(), textBlock);

            var scrollViewer = new ScrollViewer
            {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            textTab.Content = scrollViewer;
            tabControl.Items.Add(textTab);

            // Pestaña 2: Gráficos (solo si hay datos de BT)
            if (btDosesPerFraction != null && btDosesPerFraction.Any())
            {
                var chartTab = new TabItem { Header = "Gráficos" };
                var chartStack = new StackPanel { Orientation = Orientation.Vertical };

                // Gráfico de dosis por fracción
                var doseChartContainer = new GroupBox
                {
                    Header = "Dosis por Fracción en Braquiterapia",
                    Margin = new Thickness(10),
                    Content = CreateBarChart(btDosesPerFraction, false)
                };

                chartStack.Children.Add(doseChartContainer);

                // Gráfico de EQD2 acumulado
                var eqd2ChartContainer = new GroupBox
                {
                    Header = "EQD2 Acumulado por Estructura",
                    Margin = new Thickness(10),
                    Content = CreateBarChart(btDosesPerFraction, true)
                };

                chartStack.Children.Add(eqd2ChartContainer);

                chartTab.Content = new ScrollViewer
                {
                    Content = chartStack,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                tabControl.Items.Add(chartTab);
            }

            grid.Children.Add(tabControl);

            // Panel de botones
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(5)
            };

            var btnTxt = new Button { Content = "Exportar a TXT", Margin = new Thickness(5), Width = 120 };
            btnTxt.Click += (s, e) => SaveAsTxt(sb);

            buttonPanel.Children.Add(btnTxt);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            window.Content = grid;
            window.ShowDialog();
        }

        // Métodos auxiliares para la generación de gráficos
        private Canvas CreateBarChart(Dictionary<string, List<double>> data, bool isTotalEQD2)
        {
            const double margin = 40;
            const double barWidth = 30;
            const double spacing = 10;
            const double chartHeight = 300;
            const double chartWidth = 800;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Background = Brushes.White
            };

            // Calcular valores máximos para escalado
            double maxValue = isTotalEQD2 ?
                CalculateTotalEQD2Values(data).Max(x => x.Value) :
                data.Max(kvp => kvp.Value.DefaultIfEmpty().Max());

            maxValue = Math.Ceiling(maxValue * 1.1); // Añadir 10% de margen

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
                                    Text = value.ToString("F2"),
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
                    return new SolidColorBrush(Color.FromRgb(60, 179, 113)); // MediumSeaGreen
                case "Vejiga-HDR":
                    return new SolidColorBrush(Color.FromRgb(238, 130, 238)); // Violet
                case "Sigma-HDR":
                    return new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange
                default:
                    return Brushes.Gray;
            }
        }
    }
}
