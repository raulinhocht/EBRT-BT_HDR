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

[assembly: AssemblyVersion("1.0.0.9")]
[assembly: AssemblyFileVersion("1.0.0.9")]
[assembly: AssemblyInformationalVersion("1.0")]

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
            foreach (Course course in context.Patient.Courses)
            {
                if (IsEBRTCourse(course.Id))
                {
                    ProcessEBRTCourse(course, sb, ref totalDosisEBRT, ref totalFraccionesEBRT, eqd2Total);
                }
                else if (IsBrachyCourse(course.Id))
                {
                    ProcessBrachyCourse(course, sb, ref totalDosisBT, ref totalFraccionesBT, eqd2Total);
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
            // Mostrar el reporte en una ventana
            //----------------------------------------------------------------------------------------------------------------------
            ShowReportWindow(sb);
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
            sb.AppendLine("\n========================== SECCIÓN EBRT ==========================");

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
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura  | Dosis[Gy] | EQD2 [Gy]   |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");

            var structures = new[]
            {
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

                sb.AppendLine($"| {structureId,-12} | {doseAtVolume / 100,-20:F2} | {eqd2,-10:F2} |");
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
        private void ProcessBrachyCourse(Course course, StringBuilder sb, ref double totalDosisBT, ref int totalFraccionesBT, Dictionary<string, double> eqd2Total)
        {
            var plans = course.BrachyPlanSetups.OrderBy(p => p.Id).ToList();
            if (!plans.Any()) return;

            sb.AppendLine("\n====================== SECCIÓN HDR-BT ======================");
            // En ProcessBrachyCourse (al inicio):
            sb.AppendLine($"║   HDR-BT: {totalDosisBT:F2} Gy en {totalFraccionesBT} fx   ║");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            sb.Append("|Estruc/Dosis [Gy]");

            for (int i = 1; i <= plans.Count; i++)
            {
                sb.Append($"| Fx #{i} | EQD2 {i} ");
            }
            sb.AppendLine("|   totalBED  |  totalEQD2  |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            var structures = new[]
            {
                ("HR-CTV", targetVolumeRel90, alphaBetaTumor, "PTV+CTV"),
                ("Recto-HDR", targetVolumeAbs2, alphaBetaOAR, "Recto"),
                ("Vejiga-HDR", targetVolumeAbs2, alphaBetaOAR, "Vejiga"),
                ("Sigma-HDR", targetVolumeAbs2, alphaBetaOAR, "Sigma")
            };

            foreach (var (structureId, defaultVolume, alphaBeta, key) in structures)
            {
                sb.Append($"| {structureId,-15} ");
                double totalBED = 0, totalEQD2 = 0;

                foreach (var plan in plans)
                {
                    double volumeToUse = defaultVolume;
                    if (structureId == "HR-CTV")
                    {
                        var estructura = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
                        if (estructura != null)
                            volumeToUse = estructura.Volume * 0.9; // 90% del volumen
                    }

                    double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, volumeToUse);
                    double dosePerFraction = doseAtVolume / 100.0 / (double)plan.NumberOfFractions;
                    double bed = CalculateBEDWithTimeAdjustment(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta, totalTime, Tdelay, k);
                    double eqd2 = CalculateEQD2(bed, alphaBeta);

                    totalBED += bed;
                    totalEQD2 += eqd2;
                    sb.Append($"| {doseAtVolume / 100,-12:F2} | {eqd2,-12:F2} ");
                }
                sb.Append($"| {totalBED,-10:F2} | {totalEQD2,-10:F2} |");
                sb.AppendLine();
                eqd2Total[key] += totalEQD2;
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
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
            sb.AppendLine(" PRUEBA V33 Resumen Consolidado de Datos EQD2, Ajuste por Tiempo y Evaluación");
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
                    sb.AppendLine($"| {structureId,-15} | {eqd2Val,-16:F2} | {constraint.aimText,-11} | {constraint.limitText,-11} | {concepto,-32} |");
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
                    if (evaluacion.StartsWith("No Aprobado"))
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

        private void ShowReportWindow(StringBuilder sb)
        {
            var window = new Window
            {
                Title = "Evaluación Dosimétrica - V33",
                Width = 1100,
                Height = 750,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Brushes.WhiteSmoke
            };

            var textBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(15),
                TextWrapping = TextWrapping.Wrap
            };

            var scrollViewer = new ScrollViewer
            {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Procesar el texto con colores
            ProcessColoredText(sb.ToString(), textBlock);

            window.Content = scrollViewer;
            window.ShowDialog();
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

    }
}