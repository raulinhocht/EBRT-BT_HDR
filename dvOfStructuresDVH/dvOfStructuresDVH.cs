using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
// [assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
  public class Script
  {
    public Script()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Execute(ScriptContext context /*, System.Windows.Window window, ScriptEnvironment environment*/)
    {
        //CODE 10 - DyV de estructuras DVH EBRT y BT ------------------------------------------------------------------------20241217

        foreach (Course curso in context.Patient.Courses)
        {
            if (curso.Id == "1. Cervix")
            {
                foreach (var planext in curso.ExternalPlanSetups)
                {
                    MessageBox.Show($"Course: {curso.Id}"); //nombre del curso
                    MessageBox.Show($"Plan EBRT: {planext.Id}"); //nombre del plan

                    Structure PTV = planext.StructureSet.Structures.FirstOrDefault(xP => xP.Id == "PTV_56");
                    Structure Recto = context.StructureSet.Structures.FirstOrDefault(xR => xR.Id == "Recto");
                    Structure Vejiga = context.StructureSet.Structures.FirstOrDefault(xV => xV.Id == "Vejiga");
                    Structure Sigma = context.StructureSet.Structures.FirstOrDefault(xS => xS.Id == "Sigma");

                    // Obtener los datos del DVH
                    DVHData dvhDataP = planext.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataR = planext.GetDVHCumulativeData(Recto, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataV = planext.GetDVHCumulativeData(Vejiga, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataS = planext.GetDVHCumulativeData(Sigma, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                    if (dvhDataP != null)
                    {
                        // Buscar punto en el DVH
                        double dosisP = 5500; // Dosis PTV Ref 95.85
                        double dosisR = 1500; // Dosis RECTO Ref 45.36
                        double dosisV = 1100; // Dosis VEJIGA Ref 282.54
                        double dosisS = 2200; // Dosis SIGMA Ref 35.5
                        DVHPoint? P90E0 = dvhDataP.CurveData.FirstOrDefault(ptoP => Math.Abs(ptoP.DoseValue.Dose - dosisP) < 0.01);//****************************************
                        DVHPoint P90 = dvhDataP.CurveData.FirstOrDefault(ptoP => Math.Abs(ptoP.DoseValue.Dose - dosisP) < 0.01);
                        DVHPoint R90 = dvhDataR.CurveData.FirstOrDefault(ptoR => Math.Abs(ptoR.DoseValue.Dose - dosisR) < 0.01);
                        DVHPoint V90 = dvhDataV.CurveData.FirstOrDefault(ptoV => Math.Abs(ptoV.DoseValue.Dose - dosisV) < 0.01);
                        DVHPoint S90 = dvhDataS.CurveData.FirstOrDefault(ptoS => Math.Abs(ptoS.DoseValue.Dose - dosisS) < 0.01);

                        if (P90E0 != null)
                        {
                            MessageBox.Show(
                                $"PTV----- Dosis: {dosisP} cGy, Volumen: {P90.Volume:F2} cm許n" +
                                $"RECTO----- Dosis: {dosisR} cGy, Volumen: {R90.Volume:F2} cm許n" +
                                $"VEJIGA----- Dosis: {dosisV} cGy, Volumen: {V90.Volume:F2} cm許n" +
                                $"SIGMA----- Dosis: {dosisS} cGy, Volumen: {S90.Volume:F2} cm許n");
                        }
                    }
                    else
                    {
                        MessageBox.Show("No hay datos disponibles");
                    }
                }
            }
            else if (curso.Id == "2. Fletcher")
            {
                foreach (var planbt in curso.BrachyPlanSetups) // Accede a planes BT|
                {
                    MessageBox.Show($"Course: {curso.Id}"); // Nombre del curso asociado
                    MessageBox.Show($"Plan BT: {planbt.Id}"); // Nombre del plan de braquiterapia

                    // Obtener la estructura HR-CTV
                    Structure CTV = planbt.StructureSet.Structures.FirstOrDefault(xC => xC.Id == "HR-CTV");
                    Structure Recto = planbt.StructureSet.Structures.FirstOrDefault(xR => xR.Id == "Recto-HDR");
                    Structure Vejiga = planbt.StructureSet.Structures.FirstOrDefault(xV => xV.Id == "Vejiga-HDR");
                    Structure Sigma = planbt.StructureSet.Structures.FirstOrDefault(xS => xS.Id == "Sigma-HDR");

                    // Obtener los datos del DVH
                    DVHData dvhDataC = planbt.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataR = planbt.GetDVHCumulativeData(Recto, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataV = planbt.GetDVHCumulativeData(Vejiga, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataS = planbt.GetDVHCumulativeData(Sigma, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                    if (dvhDataC != null)
                    {
                        // Buscar punto en el DVH
                        double dosisC = 510; // Dosis CTV Ref 46
                        double dosisR = 50; // Dosis RECTO Ref 60.9
                        double dosisV = 80; // Dosis VEJIGA Ref 237.2
                        double dosisS = 40; // Dosis SIGMA Ref 9.86
                        DVHPoint? C90BT0 = dvhDataC.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisC) < 0.01);//****************************************
                        DVHPoint C90 = dvhDataC.CurveData.FirstOrDefault(ptoC => Math.Abs(ptoC.DoseValue.Dose - dosisC) < 0.01);
                        DVHPoint R90 = dvhDataR.CurveData.FirstOrDefault(ptoR => Math.Abs(ptoR.DoseValue.Dose - dosisR) < 0.01);
                        DVHPoint V90 = dvhDataV.CurveData.FirstOrDefault(ptoV => Math.Abs(ptoV.DoseValue.Dose - dosisV) < 0.01);
                        DVHPoint S90 = dvhDataS.CurveData.FirstOrDefault(ptoS => Math.Abs(ptoS.DoseValue.Dose - dosisS) < 0.01);

                        if (C90BT0 != null)
                        {
                            MessageBox.Show(
                                $"PTV----- Dosis: {dosisC} cGy, Volumen: {C90.Volume:F2} cm許n" +
                                $"RECTO----- Dosis: {dosisR} cGy, Volumen: {R90.Volume:F2} cm許n" +
                                $"VEJIGA----- Dosis: {dosisV} cGy, Volumen: {V90.Volume:F2} cm許n" +
                                $"SIGMA----- Dosis: {dosisS} cGy, Volumen: {S90.Volume:F2} cm許n");
                        }
                    }
                    else
                    {
                        MessageBox.Show("No hay datos disponibles");
                    }
                }
            }
        }
        }
  }
}
