//Tata_code
using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace VMS.TPS
{
    public class Script
    {
        // Clase para almacenar los datos relevantes del paciente y las estructuras (obtener y modificar)
        public class BrachytherapyData
        {
            public string Identificacion { get; set; }
            public string Nombre { get; set; }
            public int Edad { get; set; }
            public double D95CTV { get; set; }
            public double D2ccRecto { get; set; }
            public double D2ccVejiga { get; set; }
            public double D2ccSigma { get; set; }
            public double VCTV { get; set; }
            public double VISOCTV { get; set; }
            public double VRecto { get; set; }
            public double VVejiga { get; set; }
            public double VSigma { get; set; }
        }

        public void Execute(ScriptContext context)
        {
            // Verifica si hay un plan cargado en el contexto
            if (context == null || context.PlanSetup == null)
            {
                throw new ApplicationException("No hay un plan cargado.");
            }

            PlanSetup plan = context.PlanSetup;
            Patient patient = context.Patient;

            // Verifica si la información del paciente está disponible
            if (patient == null || patient.DateOfBirth == null)
            {
                throw new ApplicationException("Información del paciente no disponible.");
            }

            // Verifica si el conjunto de estructuras está disponible
            if (plan.StructureSet == null)
            {
                throw new ApplicationException("El conjunto de estructuras no está disponible.");
            }

            // Buscar la estructura CTV con cualquier sufijo HDR
            var ctvStructure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.StartsWith("HR-CTV", StringComparison.OrdinalIgnoreCase));
            string ctvId = ctvStructure != null ? ctvStructure.Id : "HR-CTV";

            //Buscar la curva de isodosis requerida en todo el body
            //double prescribedDose = Math.Round(plan.TotalDose.Dose, 2); // Redondea a 2 decimales
            Structure body = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals("BODY", StringComparison.OrdinalIgnoreCase));

            if (body == null)
            {
                MessageBox.Show("No se encontró la estructura 'BODY'.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            double VISOCTV = Math.Round(plan.GetVolumeAtDose(body, new DoseValue(700, DoseValue.DoseUnit.cGy), VolumePresentation.AbsoluteCm3), 2);
            // Crea una instancia con los datos recopilados
            var data = new BrachytherapyData
            {
                Identificacion = patient.Id,
                Nombre = string.Format("{0} {1}", patient.FirstName, patient.LastName),
                Edad = DateTime.Now.Year - patient.DateOfBirth.Value.Year,
                //Diagnostico = patient.History ?? "No disponible",
                D95CTV = Math.Round(GetDoseAtVolume(plan, ctvId, 95), 2),

                //DOSIS OARs
                D2ccRecto = Math.Round(GetDoseAtVolumeAbsoluta(plan, "Recto-HDR", 2), 2),
                D2ccVejiga = Math.Round(GetDoseAtVolumeAbsoluta(plan, "Vejiga-HDR", 2), 2),
                D2ccSigma = Math.Round(GetDoseAtVolumeAbsoluta(plan, "Sigma-HDR", 2), 2),


                VCTV = Math.Round(GetStructureVolume(plan, ctvId), 2),

                //VISOCTV = Math.Round(GetVolumeAtDose(plan, ctvId, 700), 2),

                //VOLUMENES OARs
                VRecto = Math.Round(GetStructureVolume(plan, "Recto-HD"), 2),
                VVejiga = Math.Round(GetStructureVolume(plan, "Vejiga-HDR"), 2),
                VSigma = Math.Round(GetStructureVolume(plan, "Sigma-HDR"), 2)
            };

            // Muestra los resultados en una ventana
            ShowResults(data);
        }

        // Método para obtener la dosis recibida por un volumen específico de una estructura  ---------------------------------------------------
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

        // Método para obtener el volumen de una estructura específica ---------------------------------------------------------------------------
        private double GetStructureVolume(PlanSetup plan, string structureId)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals(structureId, StringComparison.OrdinalIgnoreCase));
            return structure != null ? structure.Volume : 0;
        }

        // Método para obtener el volumen de una estructura a una dosis específica ---------------------------------------------------------------
        private double GetVolumeAtDose(PlanSetup plan, string structureId, double dose)
        {
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals(structureId, StringComparison.OrdinalIgnoreCase));
            if (structure == null) return 0;
            return plan.GetVolumeAtDose(structure, new DoseValue(dose, DoseValue.DoseUnit.cGy), VolumePresentation.AbsoluteCm3);
        }

        // Método para mostrar los resultados en una ventana con una tabla ------------------------------------------------------------------------
        private void ShowResults(BrachytherapyData data)
        {
            Window window = new Window
            {
                Title = "Datos de Braquiterapia",
                Width = 800,
                Height = 400
            };

            System.Windows.Controls.DataGrid dataGrid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = true,
                ItemsSource = new List<BrachytherapyData> { data }
            };

            window.Content = dataGrid;
            window.ShowDialog();
        }
    }
}