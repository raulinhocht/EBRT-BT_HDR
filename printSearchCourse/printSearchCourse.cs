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
        //CODE 7 - Impresión busqueda curso ------------------------------------------------------------------------------------------

        foreach (Course curso in context.Patient.Courses)
        {
            if (curso.Id == "1. Cervix")
            {
                var planext = curso.ExternalPlanSetups.FirstOrDefault(p => p.Id == "Cervix_56Gy");
                MessageBox.Show($"Plan encontrado External RT: {planext.Id}"); //nombre del plan en EBRT

            }

            else if (curso.Id == "2. Fletcher")
            {
                foreach (var device2 in context.PlanSetup.Course.BrachyPlanSetups)
                {
                    MessageBox.Show($"Plan BT: {device2.Id}"); //nombre del plan BT
                }
            }
            else
                MessageBox.Show("ninguno");
        }
    }
  }
}
