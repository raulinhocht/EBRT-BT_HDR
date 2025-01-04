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
            //CODE 8 - Obtener datos de estructuras a partir de la dosis ---------------------------------------------------------------------20241217

            foreach (Course curso in context.Patient.Courses)
            {
                // Validar curso "CURSO EBRT"
                if (curso.Id == "1. Cervix")
                {
                    MessageBox.Show("curso: EBRT");
                    var planext = curso.ExternalPlanSetups.FirstOrDefault(p => p.Id == "Cervix_56Gy");
                    MessageBox.Show($"Plan encontrado EBRT: {planext.Id}"); //nombre del plan en EBRT

                    // Obtener DVH para las estructuras
                    Structure PTV = context.StructureSet.Structures.FirstOrDefault(xP => xP.Id == "PTV_56");
                    Structure Recto = context.StructureSet.Structures.FirstOrDefault(xR => xR.Id == "Recto");
                    Structure Vejiga = context.StructureSet.Structures.FirstOrDefault(xV => xV.Id == "Vejiga");
                    Structure Sigma = context.StructureSet.Structures.FirstOrDefault(xS => xS.Id == "Sigma");
                    DVHData dvhDataP = context.PlanSetup.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataR = context.PlanSetup.GetDVHCumulativeData(Recto, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataV = context.PlanSetup.GetDVHCumulativeData(Vejiga, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataS = context.PlanSetup.GetDVHCumulativeData(Sigma, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                    if (dvhDataP == null)
                    {
                        MessageBox.Show("NE datos en HDV");
                    }

                    // Buscar punto en DVH
                    double dosisP = 5567;
                    double dosisR = 1000;
                    double dosisV = 1000;
                    double dosisS = 1000;
                    DVHPoint P90 = dvhDataP.CurveData.FirstOrDefault(ptoP => ptoP.DoseValue.Dose == dosisP);
                    DVHPoint R90 = dvhDataR.CurveData.FirstOrDefault(ptoR => ptoR.DoseValue.Dose == dosisR);
                    DVHPoint V90 = dvhDataV.CurveData.FirstOrDefault(ptoV => ptoV.DoseValue.Dose == dosisV);
                    DVHPoint S90 = dvhDataS.CurveData.FirstOrDefault(ptoS => ptoS.DoseValue.Dose == dosisS);

                    if (P90.Volume > 0)
                    {
                        MessageBox.Show($"Volumen para dosis {dosisP}: {P90.Volume:F4} cm³ \n" +
                            $" Volumen para dosis {dosisR}: {R90.Volume:F4} cm³ \n" +
                            $" Volumen para dosis {dosisV}: {V90.Volume:F4} cm³ \n" +
                            $" Volumen para dosis {dosisS}: {S90.Volume:F4} cm³");
                    }
                    else
                    {
                        MessageBox.Show("NE volumen");
                    }

                }

                // Validar curso "CURSO BT"
                else if (curso.Id == "2. Fletcher")
                {
                    MessageBox.Show("curso: BT");
                    foreach (var planbt in context.PlanSetup.Course.BrachyPlanSetups)
                    {
                        MessageBox.Show($"Plan encontrado BT: {planbt.Id}"); //nombre del plan BT

                        // Obtener DVH para la estructura CTV
                        //Structure CTV = context.StructureSet.Structures.FirstOrDefault(x => x.Id == "HR-CTV");
                        //DVHData dvhDataBT = context.PlanSetup.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                        //planbt in context.PlanSetup.Course.BrachyPlanSetups
                        Structure CTV = planbt.StructureSet.Structures.FirstOrDefault(x => x.Id == "HR-CTV");

                        DVHData dvhDataBT = context.PlanSetup.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                        MessageBox.Show($"Volumen {CTV.Volume}");

                        if (dvhDataBT == null)
                        {
                            MessageBox.Show("NE datos en HDV");
                        }

                        // Buscar punto en DVH
                        double dosisBT = 579;
                        DVHPoint C90BT = dvhDataBT.CurveData.FirstOrDefault(pto => pto.DoseValue.Dose == dosisBT);


                        if (C90BT.Volume > 0)
                        {
                            MessageBox.Show($"Volumen para dosis {dosisBT}: {C90BT.Volume:F4} cm³");
                        }
                        else
                        {
                            MessageBox.Show($"NE volumen");
                        }


                    }
                }
                else
                    MessageBox.Show("ninguno");

            }
    }
  }
}
