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
            //CODE 9 - Dosis y volumen de una estructura DVH -------------------------------------------------------20241223 OK

            foreach (Course curso in context.Patient.Courses)
            {
                if (curso.Id == "1. Cervix")
                {
                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        MessageBox.Show($"Course: {curso.Id}"); //nombre del curso
                        MessageBox.Show($"Plan EBRT: {planext.Id}"); //nombre del plan

                        Structure PTV = planext.StructureSet.Structures.FirstOrDefault(x => x.Id == "PTV_56");

                        // Obtener los datos del DVH
                        DVHData dvhDataE = planext.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                        if (dvhDataE != null)
                        {
                            // Buscar punto en el DVH
                            double dosisE = 579; // Dosis
                            DVHPoint P90E = dvhDataE.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisE) < 0.01);
                            DVHPoint? P90E0 = dvhDataE.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisE) < 0.01);//------------------------------------------------------------

                            if (P90E0 != null)
                            {
                                MessageBox.Show($"Dosis: {dosisE} cGy \nVolumen correspondiente: {P90E.Volume:F2} cm³");
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
                    foreach (var planbt in curso.BrachyPlanSetups) // Accede a planes BT
                    {
                        MessageBox.Show($"Course: {curso.Id}"); // Nombre del curso asociado
                        MessageBox.Show($"Plan BT: {planbt.Id}"); // Nombre del plan de braquiterapia

                        // Obtener la estructura HR-CTV
                        Structure CTV = planbt.StructureSet.Structures.FirstOrDefault(x => x.Id == "HR-CTV");

                        // Obtener los datos del DVH
                        DVHData dvhDataBT = planbt.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                        if (dvhDataBT != null)
                        {
                            // Buscar punto en el DVH
                            double dosisBT = 579; // Dosis
                            DVHPoint C90BT = dvhDataBT.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisBT) < 0.01);
                            DVHPoint? C90BT0 = dvhDataBT.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisBT) < 0.01);//------------------------------------------------------------

                            if (C90BT0 != null)
                            {
                                MessageBox.Show($"Dosis: {dosisBT} cGy \nVolumen correspondiente: {C90BT.Volume:F2} cm³");
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
