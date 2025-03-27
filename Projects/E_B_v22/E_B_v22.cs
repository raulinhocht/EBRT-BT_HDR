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

[assembly: AssemblyVersion("1.0.0.6")]
[assembly: AssemblyFileVersion("1.0.0.6")]
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
            sb.AppendLine(" PRUEBA V23 - Resumen Consolidado de Datos con EQD2 ");
            sb.AppendLine("=====================================================================================");

            // Diccionario para EQD2 total de EBRT + HDR-BT
            Dictionary<string, double> eqd2Total = new Dictionary<string, double>
            {
                {"PTV+CTV", 0}, {"Recto", 0}, {"Vejiga", 0}, {"Sigma", 0}
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
                        ProcessEBRTPlan(planext, sb, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeAbs2, eqd2Total);
                    }
                }
                else if (curso.Id == "2. Fletcher")
                {
                    sb.AppendLine("\n====================== SECCIÓN HDR-BT ======================");
                    ProcessBrachytherapyPlans(curso, sb, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeAbs2, eqd2Total);
                }
            }

            // --------------------- SECCIÓN TOTAL EBRT + HDR-BT ---------------------
            sb.AppendLine("\n====================== SECCIÓN TOTAL EBRT + HDR-BT ======================");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       | EQD2 Total (Gy)  |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
            foreach (var item in eqd2Total)
            {
                sb.AppendLine($"| {item.Key,-15} | {item.Value,-16:F2} |");
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------");

            // Mostrar resultados en una ventana con scroll para mejor visualización
            Window messageWindow = new Window
            {
                Title = "Resumen de Datos con EQD2",
                Content = new TextBox
                {
                    Text = sb.ToString(),
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Width = 900,
                Height = 700
            };

            messageWindow.ShowDialog();
        }

        private void ProcessBrachytherapyPlans(Course course, StringBuilder sb, double alphaBetaTumor, double alphaBetaOAR,
            double targetVolumeRel90, double targetVolumeAbs2, Dictionary<string, double> eqd2Total)
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
                // Para HR-CTV, se utilizará V90 (90% del volumen total de la estructura)
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
                    // Para HR-CTV, recalculamos el volumen como el 90% del volumen total
                    double volumeToUse = defaultVolume;
                    if (structureId == "HR-CTV")
                    {
                        var estructura = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
                        if (estructura != null)
                        {
                            volumeToUse = estructura.Volume * 0.9;
                        }
                    }

                    double doseAtVolume = GetDoseAtVolume(plan, structureId, volumeToUse);
                    double dosePerFraction = doseAtVolume / (double)plan.NumberOfFractions;
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

        private void ProcessEBRTPlan(ExternalPlanSetup plan, StringBuilder sb, double alphaBetaTumor, double alphaBetaOAR,
            double targetVolumeRel90, double targetVolumeAbs2, Dictionary<string, double> eqd2Total)
        {
            sb.AppendLine($"Dosis Prescrita: {plan.TotalDose} en {plan.NumberOfFractions} fracciones");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura  | Dosis at Volume (Gy) | EQD2 (Gy)   |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");

            var structures = new[]
            {
                ("PTV_56", targetVolumeRel90, alphaBetaTumor, "PTV+CTV"),
                ("Recto", targetVolumeAbs2, alphaBetaOAR, "Recto"),
                ("Vejiga", targetVolumeAbs2, alphaBetaOAR, "Vejiga"),
                ("Sigma", targetVolumeAbs2, alphaBetaOAR, "Sigma")
            };

            foreach (var (structureId, targetVolume, alphaBeta, key) in structures)
            {
                double eqd2 = ProcessEQD2ForPlan(plan, structureId, targetVolume, alphaBeta);
                double doseAtVolume = GetDoseAtVolume(plan, structureId, targetVolume) / 100.0;
                sb.AppendLine($"| {structureId,-12} | {doseAtVolume,-20:F2} | {eqd2,-10:F2} |");
                eqd2Total[key] += eqd2;
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
        }

        private double ProcessEQD2ForPlan(PlanSetup plan, string structureId, double targetVolume, double alphaBeta)
        {
            double doseAtVolume = GetDoseAtVolume(plan, structureId, targetVolume);
            double dosePerFraction = doseAtVolume / (double)plan.NumberOfFractions;
            double bed = CalculateBED(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta);
            return CalculateEQD2(bed, alphaBeta);
        }

        private double GetDoseAtVolume(PlanSetup plan, string structureId, double volume)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            return structure == null ? 0 : plan.GetDoseAtVolume(structure, volume, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute).Dose;
        }

        private double CalculateBED(double dosePerFraction, double fractions, double alphaBeta)
        {
            return (dosePerFraction / 100) * fractions * (1 + (dosePerFraction / 100) / alphaBeta);
        }

        private double CalculateEQD2(double bed, double alphaBeta)
        {
            return bed / (1 + 2 / alphaBeta);
        }
    }
}
// v100 A V90


