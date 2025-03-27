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

            List<BrachytherapyData> results = new List<BrachytherapyData>();

            foreach (Course curso in context.Patient.Courses)
            {
                double alphaBetaTumor = 10, alphaBetaOAR = 3;
                double targetVolumeRel90 = 90;
                double targetVolumeAbs2 = 2;

                if (curso.Id == "1. Cervix")
                {
                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        ProcessPlan(planext, results, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeAbs2);
                    }
                }
                else if (curso.Id == "2. Fletcher")
                {
                    foreach (var planbt in curso.BrachyPlanSetups)
                    {
                        ProcessPlan(planbt, results, alphaBetaTumor, alphaBetaOAR, targetVolumeRel90, targetVolumeAbs2);
                    }
                }
            }

            ShowResults(results);
        }

        private void ProcessPlan(PlanSetup plan, List<BrachytherapyData> results, double alphaBetaTumor, double alphaBetaOAR, double targetVolumeRel90, double targetVolumeAbs2)
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

                results.Add(new BrachytherapyData
                {
                    Estructura = structureId,
                    Dosis = doseAtVolume,
                    Volumen = targetVolume,
                    BED = bed,
                    EQD2 = eqd2
                });
            }
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

        private void ShowResults(List<BrachytherapyData> results)
        {
            Window window = new Window
            {
                Title = "Resultados de BED y EQD2",
                Width = 900,
                Height = 700
            };

            DataGrid dataGrid = new DataGrid
            {
                AutoGenerateColumns = true,
                ItemsSource = results
            };

            window.Content = dataGrid;
            window.ShowDialog();
        }
    }
}
