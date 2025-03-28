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
            foreach (Course curso in context.Patient.Courses)
            {
                double alphaBetaTumor = 10, alphaBetaOAR = 3;
                double targetVolumeRel90 = 90;  // 90%
                double targetVolumeRel100 = 100; // 100%
                double targetVolumeAbs2 = 2;      // 2 Gy

                if (curso.Id == "1. Cervix")
                {
                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        ProcessExternalPlan(planext, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeRel100, targetVolumeAbs2);
                    }
                }
                else if (curso.Id == "2. Fletcher")
                {
                    foreach (var planbt in curso.BrachyPlanSetups)
                    {
                        ProcessBrachyPlan(planbt, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeRel100, targetVolumeAbs2);
                    }
                }
            }
        }

        private void ProcessExternalPlan(ExternalPlanSetup plan, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeRel100, double targetVolumeAbs2)
        {
            // Estructuras para radioterapia externa
            var structures = new[]
            {
                ("PTV_56", targetVolumeRel100, alphaBetaTumor),
                ("PTV_56", targetVolumeRel90, alphaBetaTumor),
                ("Recto", targetVolumeAbs2, alphaBetaOAR),
                ("Vejiga", targetVolumeAbs2, alphaBetaOAR),
                ("Sigma", targetVolumeAbs2, alphaBetaOAR)
            };

            ProcessPlan(plan, structures);
        }

        private void ProcessBrachyPlan(BrachyPlanSetup plan, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeRel100, double targetVolumeAbs2)
        {
            // Estructuras para braquiterapia
            var structures = new[]
            {
                ("HR-CTV", targetVolumeRel100, alphaBetaTumor),
                ("HR-CTV", targetVolumeRel90, alphaBetaTumor),
                ("Recto-HDR", targetVolumeAbs2, alphaBetaOAR),
                ("Vejiga-HDR", targetVolumeAbs2, alphaBetaOAR),
                ("Sigma-HDR", targetVolumeAbs2, alphaBetaOAR)
            };

            ProcessPlan(plan, structures);
        }

        private void ProcessPlan(PlanSetup plan, (string StructureId, double TargetVolume, double AlphaBeta)[] structures)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Plan: {plan.Id}");
            sb.AppendLine($"Dosis prescrita: {plan.TotalDose}");
            sb.AppendLine($"N�mero de fracciones: {plan.NumberOfFractions}");
            sb.AppendLine("-------------------------------------------------------------------------------");
            sb.AppendLine("|   Estructura   |   Dosis (Gy)   |   Volumen (cm�)   |   BED (Gy)   | EQD2 |");
            sb.AppendLine("-------------------------------------------------------------------------------");

            foreach (var (structureId, targetVolume, alphaBeta) in structures)
            {
                var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
                if (structure == null) continue;

                var dvhData = plan.GetDVHCumulativeData(structure, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 2);
                //-------------------------------------------------------------
                if (dvhData == null)
                {
                    sb.AppendLine($"| {structureId,-15} | {"Sin datos DVH",-14} | {"N/A",-16} | {"N/A",-10} | {"N/A",-5} |");
                    continue;
                }
                //------------------------------------------------------------
                var dvhPoint = dvhData?.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolume) < 0.5);//------------------- REF: 0.1

                if (dvhPoint != null)
                {
                    var dosePerFraction = dvhPoint.Value.DoseValue.Dose / plan.NumberOfFractions;

                    var bed = CalculateBED((double)dosePerFraction, (double)plan.NumberOfFractions, alphaBeta);
                    var eqd2 = CalculateEQD2(bed, alphaBeta);

                    sb.AppendLine($"| {structureId,-15} | {dvhPoint.Value.DoseValue.Dose * 0.01,-14:F2} | {targetVolume,-16:F2} | {bed,-10:F2} | {eqd2,-5:F2} |");
                }
                //-------------------
                if (dvhPoint == null)
                {
                    sb.AppendLine($"| {structureId,-15} | {"Punto no encontrado",-14} | {"N/A",-16} | {"N/A",-10} | {"N/A",-5} |");
                    continue;
                }
                //-------------------
            }

            sb.AppendLine("-------------------------------------------------------------------------------");
            MessageBox.Show(sb.ToString(), "Resumen de Datos");
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