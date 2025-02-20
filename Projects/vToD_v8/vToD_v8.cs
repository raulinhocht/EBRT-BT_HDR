using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Collections.Generic;

[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

namespace VMS.TPS
{
    public class Script
    {
        // Clase para almacenar los datos relevantes del paciente y las estructuras
        public class BrachytherapyData
        {
            public string Estructura { get; set; }
            public double Dosis { get; set; }
            public double Volumen { get; set; }
            public double BED { get; set; }
            public double EQD2 { get; set; }
        }

        public Script() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
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
            ProcessPlan(plan, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeRel100, targetVolumeAbs2);
        }

        private void ProcessBrachyPlan(BrachyPlanSetup plan, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeRel100, double targetVolumeAbs2)
        {
            ProcessPlan(plan, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeRel100, targetVolumeAbs2);
        }

        private void ProcessPlan(PlanSetup plan, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeRel100, double targetVolumeAbs2)
        {
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

            var results = new List<BrachytherapyData>();

            foreach (var (structureId, targetVolume, alphaBeta) in structures)
            {
                var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);

                if (structure == null) continue;

                double dose = 0;
                if (targetVolume == targetVolumeAbs2)
                {
                    dose = GetDoseAtVolumeAbsoluta(plan, structureId, targetVolume);
                }
                else
                {
                    dose = GetDoseAtVolume(plan, structureId, targetVolume);
                }

                double bed = CalculateBED(dose, (double)plan.NumberOfFractions, alphaBeta);
                double eqd2 = CalculateEQD2(bed, alphaBeta);

                results.Add(new BrachytherapyData
                {
                    Estructura = structureId,
                    Dosis = dose,
                    Volumen = targetVolume,
                    BED = bed,
                    EQD2 = eqd2
                });
            }

            ShowResults(results);
        }

        // Métodos auxiliares reutilizados del código anterior
        private double GetDoseAtVolume(PlanSetup plan, string structureId, double volumePercent)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals(structureId, StringComparison.OrdinalIgnoreCase));
            if (structure == null) return 0;
            var doseValue = plan.GetDoseAtVolume(structure, volumePercent, VolumePresentation.Relative, DoseValuePresentation.Absolute);
            return doseValue != null ? doseValue.Dose : 0;
        }

        private double GetDoseAtVolumeAbsoluta(PlanSetup plan, string structureId, double volumeAbsolute)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals(structureId, StringComparison.OrdinalIgnoreCase));
            if (structure == null) return 0;
            var doseValue = plan.GetDoseAtVolume(structure, volumeAbsolute, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute);
            return doseValue != null ? doseValue.Dose : 0;
        }

        private double CalculateBED(double dosePerFraction, double fractions, double alphaBeta)
        {
            return dosePerFraction * fractions * (1 + dosePerFraction / alphaBeta);
        }

        private double CalculateEQD2(double bed, double alphaBeta)
        {
            return bed / (1 + 2 / alphaBeta);
        }

        // Método para mostrar los resultados en una ventana con una tabla
        private void ShowResults(List<BrachytherapyData> data)
        {
            Window window = new Window
            {
                Title = "Resultados de BED y EQD2",
                Width = 800,
                Height = 400
            };

            System.Windows.Controls.DataGrid dataGrid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = true,
                ItemsSource = data
            };

            window.Content = dataGrid;
            window.ShowDialog();
        }
    }
}