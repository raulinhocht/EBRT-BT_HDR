using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.0.3")]
[assembly: AssemblyFileVersion("1.0.0.3")]
[assembly: AssemblyInformationalVersion("1.0")]

namespace VMS.TPS
{
    public class Script
    {
        public Script() { }

        public class BrachytherapyData
        {
            public string Estructura { get; set; }
            public double Dosis { get; set; }
            public double Volumen { get; set; }
            public double BED { get; set; }
            public double EQD2 { get; set; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            if (context?.Patient == null)
            {
                MessageBox.Show("No hay un paciente cargado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PRUEBA V20 - Resumen Consolidado de Datos con EQD2 por Fracción");
            sb.AppendLine("===============================================================================");

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
                        ProcessEBRTPlan(planext, sb, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeAbs2);
                    }
                }
                else if (curso.Id == "2. Fletcher")
                {
                    sb.AppendLine("\n====================== SECCIÓN HDR-BT ======================");
                    ProcessBrachytherapyPlans(curso, sb, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeAbs2);
                }
            }
            //--------------------------------
            //Total EBRT+HDR BRT
            sb.AppendLine("Total EBRT+HDR BRT");
            sb.AppendLine("\n====================== SECCIÓN EBRT + HDR BT ======================");
            sb.AppendLine("Total EBRT+HDR BRT");


            sb.AppendLine("===============================================================================");
            
            //---------------------------

            // Ventana con mayor tamaño para mejor visualización
            Window messageWindow = new Window
            {
                Title = "Resumen de Datos con EQD2",
                Content = new System.Windows.Controls.TextBox
                {
                    Text = sb.ToString(),
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
                },
                Width = 900,
                Height = 700
            };

            messageWindow.ShowDialog();
        }

        private void ProcessBrachytherapyPlans(Course course, StringBuilder sb, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeAbs2)
        {
            var plans = course.BrachyPlanSetups.ToList();
            if (!plans.Any()) return;

            sb.AppendLine("\nPlan de Braquiterapia - Dosis por Sesión con EQD2");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            sb.Append("| Estructura       ");
            for (int i = 1; i <= plans.Count; i++) sb.Append($"| Fx #{i} | EQD2 {i} ");
            sb.AppendLine("|   BED Total  |  EBRT+BT total  |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            var structures = new[]
            {
                ("HR-CTV", targetVolumeRel90, alphaBetaTumor),
                ("Recto-HDR", targetVolumeAbs2, alphaBetaOAR),
                ("Vejiga-HDR", targetVolumeAbs2, alphaBetaOAR),
                ("Sigma-HDR", targetVolumeAbs2, alphaBetaOAR)
            };

            foreach (var (structureId, targetVolume, alphaBeta) in structures)
            {
                sb.Append($"| {structureId,-15} ");
                double totalBED = 0, totalEQD2 = 0;

                foreach (var plan in plans)
                {
                    double doseAtVolume = GetDoseAtVolume(plan, structureId, targetVolume);
                    double dosePerFraction = doseAtVolume / (double)plan.NumberOfFractions;
                    double bed = CalculateBED(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta);
                    double eqd2 = CalculateEQD2(bed, alphaBeta);

                    totalBED += bed;
                    totalEQD2 += eqd2;
                    sb.Append($"| {doseAtVolume/100,-12:F2} | {eqd2,-12:F2} ");
                }
                sb.Append($"| {totalBED,-10:F2} | {totalEQD2,-10:F2} |");
                sb.AppendLine();
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
        }

        private void ProcessEBRTPlan(ExternalPlanSetup plan, StringBuilder sb, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeAbs2)
        {
            sb.AppendLine($"Dosis Prescrita: {plan.TotalDose} en {plan.NumberOfFractions} fracciones");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       |  Dosis (cGy) |  Volumen (cm³) |   BED (cGy)  |  EQD2  |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            ProcessPlan(plan, sb, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeAbs2);
        }

        private void ProcessPlan(PlanSetup plan, StringBuilder sb, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeAbs2)
        {
            var structures = new[]
            {
                ("PTV_56", targetVolumeRel90, alphaBetaTumor),
                ("Recto", targetVolumeAbs2, alphaBetaOAR),
                ("Vejiga", targetVolumeAbs2, alphaBetaOAR),
                ("Sigma", targetVolumeAbs2, alphaBetaOAR)
            };

            foreach (var (structureId, targetVolume, alphaBeta) in structures)
            {
                double doseAtVolume = GetDoseAtVolume(plan, structureId, targetVolume);
                double dosePerFraction = doseAtVolume / (double)plan.NumberOfFractions;
                double bed = CalculateBED(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta);
                double eqd2 = CalculateEQD2(bed, alphaBeta);

                sb.AppendLine($"| {structureId,-15} | {doseAtVolume/100,-14:F2} | {targetVolume,-16:F2} | {bed,-10:F2} | {eqd2,-5:F2} |");
                sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
        }

        private double GetDoseAtVolume(PlanSetup plan, string structureId, double volume)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            return structure == null ? 0 : plan.GetDoseAtVolume(structure, volume, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute).Dose;
        }

        private double CalculateBED(double dosePerFraction, double fractions, double alphaBeta)
        {
            return (dosePerFraction/100) * fractions * (1 + (dosePerFraction/100) / alphaBeta);
        }

        private double CalculateEQD2(double bed, double alphaBeta)
        {
            return bed / (1 + 2 / alphaBeta);
        }
    }
}
