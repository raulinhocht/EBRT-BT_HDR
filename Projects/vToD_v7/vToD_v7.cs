using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
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
            sb.AppendLine("PRUEBA V5 - Resumen de Datos");

            foreach (Course curso in context.Patient.Courses)
            {
                double alphaBetaTumor = 10, alphaBetaOAR = 3;
                double targetVolumeRel90 = 90;
                double targetVolumeRel100 = 100;
                double targetVolumeAbs2 = 2;

                if (curso.Id == "1. Cervix")
                {
                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        ProcessPlan(planext, sb, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeRel100, targetVolumeAbs2);
                    }
                }
                else if (curso.Id == "2. Fletcher")
                {
                    foreach (var planbt in curso.BrachyPlanSetups)
                    {
                        ProcessPlan(planbt, sb, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeRel100, targetVolumeAbs2);
                    }
                }
            }

            MessageBox.Show(sb.ToString(), "Resumen de Datos");
        }

        private void ProcessPlan(PlanSetup plan, StringBuilder sb, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeRel100, double targetVolumeAbs2)
        {
            if (plan?.StructureSet == null)
            {
                sb.AppendLine($"El plan {plan?.Id ?? "desconocido"} no tiene estructuras asociadas.");
                return;
            }

            var structures = new[]
            {
                ("PTV_56", targetVolumeRel100, alphaBetaTumor),
                ("PTV_56", targetVolumeRel90, alphaBetaTumor),
                ("Recto", targetVolumeAbs2, alphaBetaOAR),
                ("Vejiga", targetVolumeAbs2, alphaBetaOAR),
                ("Sigma", targetVolumeAbs2, alphaBetaOAR),
                ("HR-CTV", targetVolumeRel100, alphaBetaTumor),
                ("HR-CTV", targetVolumeRel90, alphaBetaTumor),
                ("Recto-HDR", targetVolumeAbs2, alphaBetaOAR),
                ("Vejiga-HDR", targetVolumeAbs2, alphaBetaOAR),
                ("Sigma-HDR", targetVolumeAbs2, alphaBetaOAR)
            };

            sb.AppendLine("\n-----------------------------------------------------------");
            sb.AppendLine($"Plan: {plan.Id}");
            sb.AppendLine($"Dosis prescrita: {plan.TotalDose}");
            sb.AppendLine($"Número de fracciones: {plan.NumberOfFractions}");
            sb.AppendLine("-----------------------------------------------------------");
            sb.AppendLine("|   Estructura   |   Dosis (cGy)   |   Volumen (cm³)   |   BED (cGy)   | EQD2 |");
            sb.AppendLine("-----------------------------------------------------------");

            foreach (var (structureId, targetVolume, alphaBeta) in structures)
            {
                var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals(structureId, StringComparison.OrdinalIgnoreCase));
                if (structure == null)
                {
                    sb.AppendLine($"| {structureId,-15} | No encontrada |");
                    continue;
                }

                double doseAtVolume = GetDoseAtVolume(plan, structure, targetVolume);
                if (doseAtVolume <= 0)
                {
                    sb.AppendLine($"| {structureId,-15} | No disponible |");
                    continue;
                }

                double dosePerFraction = (double)(doseAtVolume / plan.NumberOfFractions);
                double bed = CalculateBED(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta);
                double eqd2 = CalculateEQD2(bed, alphaBeta);

                sb.AppendLine($"| {structureId,-15} | {doseAtVolume,-14:F2} | {targetVolume,-16:F2} | {bed,-10:F2} | {eqd2,-5:F2} |");
            }
            sb.AppendLine("-----------------------------------------------------------");
        }

        private double GetDoseAtVolume(PlanSetup plan, Structure structure, double volume)
        {
            if (plan.Dose == null)
                return 0;

            var doseValue = plan.GetDoseAtVolume(structure, volume, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute);
            return doseValue.Dose;
        }

        private double CalculateBED(double dosePerFraction, double fractions, double alphaBeta)
        {
            return dosePerFraction * fractions * (1 + dosePerFraction / alphaBeta);
        }

        private double CalculateEQD2(double bed, double alphaBeta)
        {
            return bed / (1 + 2 / alphaBeta);
        }
    }
}
