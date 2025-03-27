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

[assembly: AssemblyVersion("1.0.0.8")]
[assembly: AssemblyFileVersion("1.0.0.8")]
[assembly: AssemblyInformationalVersion("1.0")]

namespace VMS.TPS
{
    public class Script
    {
        public Script() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            if (context?.Patient == null)
            {
                MessageBox.Show("No hay un paciente cargado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(" PRUEBA V24 - Resumen Consolidado de Datos con EQD2 + Comparación Aims/Límites y Ajuste por Tiempo");
            sb.AppendLine("=====================================================================================");

            // Diccionario para EQD2 total de EBRT + HDR-BT
            Dictionary<string, double> eqd2Total = new Dictionary<string, double>
            {
                {"PTV+CTV", 0},
                {"Recto", 0},
                {"Vejiga", 0},
                {"Sigma", 0}
            };

            // --- 1) Definimos los parámetros de Aim y Límite para cada estructura ---
            var constraints = new Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)>
            {
                { "Recto",    (65.0, 75.0, "lessThan", "< 65 Gy", "< 75 Gy") },
                { "Vejiga",   (80.0, 90.0, "lessThan", "< 80 Gy", "< 90 Gy") },
                { "Sigma",    (70.0, 75.0, "lessThan", "< 70 Gy", "< 75 Gy") },
                { "PTV+CTV",  (85.0, 95.0, "range",    "> 85 Gy", "< 95 Gy") }
            };

            foreach (Course curso in context.Patient.Courses)
            {
                double alphaBetaTumor = 10, alphaBetaOAR = 3;
                double targetVolumeRel90 = 90;
                double targetVolumeAbs2 = 2;

                if (curso.Id == "1. Cervix")
                {
                    sb.AppendLine("\n========================== SECCIÓN EBRT ==========================");
                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        ProcessEBRTPlan(planext, sb, alphaBetaTumor, alphaBetaOAR,
                                        targetVolumeRel90, targetVolumeAbs2, eqd2Total);
                    }
                }
                else if (curso.Id == "2. Fletcher")
                {
                    sb.AppendLine("\n====================== SECCIÓN HDR-BT ======================");
                    ProcessBrachytherapyPlans(curso, sb, alphaBetaTumor, alphaBetaOAR,
                                              targetVolumeRel90, targetVolumeAbs2, eqd2Total);
                }
            }

            // --------------------- SECCIÓN TOTAL EBRT + HDR-BT ---------------------
            sb.AppendLine("\n====================== SECCIÓN TOTAL EBRT + HDR-BT ======================");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       | EQD2 Total (Gy)  | Aim         | Limit        | Concepto Final                     |");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");

            foreach (var item in eqd2Total)
            {
                string structureId = item.Key;
                double eqd2Val = item.Value;

                if (constraints.TryGetValue(structureId, out var constraint))
                {
                    string concepto = EvaluateConstraints(eqd2Val, constraint);
                    sb.AppendLine(
                        $"| {structureId,-15} | {eqd2Val,-16:F2} | {constraint.aimText,-11} | {constraint.limitText,-11} | {concepto,-32} |"
                    );
                }
                else
                {
                    sb.AppendLine(
                        $"| {structureId,-15} | {eqd2Val,-16:F2} |      -      |      -      |       Sin definición        |"
                    );
                }
            }
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");

            Window messageWindow = new Window
            {
                Title = "Resumen de Datos con EQD2 + Ajuste por Tiempo y Comparación Aims/Límites",
                Content = new TextBox
                {
                    Text = sb.ToString(),
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Width = 1000,
                Height = 700
            };

            messageWindow.ShowDialog();
        }

        // Método que evalúa si un valor cumple con las restricciones clínicas
        private string EvaluateConstraints(double eqd2Val, (double aimValue, double limitValue, string type, string aimText, string limitText) constraint)
        {
            double aimVal = constraint.aimValue;
            double limitVal = constraint.limitValue;
            string tipo = constraint.type;

            if (tipo == "lessThan")
            {
                if (eqd2Val <= aimVal)
                {
                    return "Aprobado (Aim cumplido)";
                }
                else if (eqd2Val <= limitVal)
                {
                    return "Aprobado (Dentro de Límite)";
                }
                else
                {
                    return "No Aprobado (Supera Límite)";
                }
            }
            else if (tipo == "range")
            {
                if (eqd2Val >= aimVal && eqd2Val <= limitVal)
                {
                    return "Aprobado (En rango)";
                }
                else
                {
                    return "No Aprobado (Fuera de rango)";
                }
            }
            return "Sin definición";
        }

        // ------------------- Procesamiento de Braquiterapia -------------------
        private void ProcessBrachytherapyPlans(
            Course course,
            StringBuilder sb,
            double alphaBetaTumor,
            double alphaBetaOAR,
            double targetVolumeRel90,
            double targetVolumeAbs2,
            Dictionary<string, double> eqd2Total)
        {
            var plans = course.BrachyPlanSetups.ToList();
            if (!plans.Any()) return;

            sb.AppendLine("\n Plan de Braquiterapia - Dosis por Sesión con EQD2 ");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            sb.Append("| Estructura       ");
            for (int i = 1; i <= plans.Count; i++) sb.Append($"| Fx #{i} (Gy) | EQD2 {i} ");
            sb.AppendLine("|   totalBED  |  totalEQD2  |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            var structures = new[]
            {
                // Para HR-CTV se utiliza V90 (90% del volumen total)
                ("HR-CTV", targetVolumeRel90, alphaBetaTumor, "PTV+CTV"),
                ("Recto-HDR",   targetVolumeAbs2, alphaBetaOAR, "Recto"),
                ("Vejiga-HDR",  targetVolumeAbs2, alphaBetaOAR, "Vejiga"),
                ("Sigma-HDR",   targetVolumeAbs2, alphaBetaOAR, "Sigma")
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
                        {
                            volumeToUse = estructura.Volume * 0.9; // V90
                        }
                    }

                    double doseAtVolume = GetDoseAtVolume(plan, structureId, volumeToUse);
                    double dosePerFraction = doseAtVolume / (double)plan.NumberOfFractions;
                    // Para BT se utiliza el cálculo sin ajuste de tiempo
                    double bed = CalculateBED(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta);
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

        // ------------------- Procesamiento de EBRT con ajuste por tiempo -------------------
        private void ProcessEBRTPlan(
            ExternalPlanSetup plan,
            StringBuilder sb,
            double alphaBetaTumor,
            double alphaBetaOAR,
            double targetVolumeRel90,
            double targetVolumeAbs2,
            Dictionary<string, double> eqd2Total)
        {
            // Parámetros para el ajuste por tiempo (ejemplo)
            double totalTime = 35.0;  // Tiempo total del tratamiento en días
            double Tdelay = 21.0;     // Día a partir del cual se inicia la repoblación
            double k = 0.6;           // Factor de repoblación en Gy/día

            sb.AppendLine($"Dosis Prescrita: {plan.TotalDose} en {plan.NumberOfFractions} fracciones");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura  | Dosis at Volume (Gy) | EQD2 (Gy)   |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");

            var structures = new[]
            {
                ("PTV_56",   targetVolumeRel90, alphaBetaTumor, "PTV+CTV"),
                ("Recto",    targetVolumeAbs2,  alphaBetaOAR,   "Recto"),
                ("Vejiga",   targetVolumeAbs2,  alphaBetaOAR,   "Vejiga"),
                ("Sigma",    targetVolumeAbs2,  alphaBetaOAR,   "Sigma")
            };

            foreach (var (structureId, targetVolume, alphaBeta, key) in structures)
            {
                // Usamos la función modificada que incluye ajuste por tiempo
                double eqd2 = ProcessEQD2ForPlan(plan, structureId, targetVolume, alphaBeta, totalTime, Tdelay, k);
                double doseAtVolume = GetDoseAtVolume(plan, structureId, targetVolume) / 100.0;
                sb.AppendLine($"| {structureId,-12} | {doseAtVolume,-20:F2} | {eqd2,-10:F2} |");
                eqd2Total[key] += eqd2;
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
        }

        // Función para procesar EQD2 en EBRT con ajuste por tiempo
        private double ProcessEQD2ForPlan(PlanSetup plan, string structureId, double targetVolume, double alphaBeta, double totalTime, double Tdelay, double k)
        {
            double doseAtVolume = GetDoseAtVolume(plan, structureId, targetVolume);
            double dosePerFraction = doseAtVolume / (double)plan.NumberOfFractions;
            double bed = CalculateBEDWithTimeAdjustment(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta, totalTime, Tdelay, k);
            return CalculateEQD2(bed, alphaBeta);
        }

        // ------------------- Cálculos de BED y EQD2 -------------------

        // Cálculo de BED sin ajuste de tiempo (para BT)
        private double CalculateBED(double dosePerFraction, double fractions, double alphaBeta)
        {
            return (dosePerFraction / 100.0) * fractions * (1 + (dosePerFraction / 100.0) / alphaBeta);
        }

        // Cálculo de BED con ajuste por tiempo (para EBRT)
        private double CalculateBEDWithTimeAdjustment(double dosePerFraction, double fractions, double alphaBeta, double totalTime, double Tdelay, double k)
        {
            double bed = (dosePerFraction / 100.0) * fractions * (1 + (dosePerFraction / 100.0) / alphaBeta);
            if (totalTime > Tdelay)
            {
                bed -= k * (totalTime - Tdelay);
            }
            return bed;
        }

        private double CalculateEQD2(double bed, double alphaBeta)
        {
            return bed / (1 + 2.0 / alphaBeta);
        }

        // Obtención de la dosis en un volumen específico
        private double GetDoseAtVolume(PlanSetup plan, string structureId, double volume)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            if (structure == null) return 0;
            return plan.GetDoseAtVolume(structure, volume, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute).Dose;
        }
    }
}
