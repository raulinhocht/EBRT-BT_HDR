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
      // TODO : Add here the code that is called when the script is launched from Eclipse.

      //CODE 1 - Dosis a Volumen (absolutos)
            
        Structure PTV = context.StructureSet.Structures.FirstOrDefault(x => x.Id == "PTV_CMI");
            
        DVHData dvhData = context.PlanSetup.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
        DVHPoint pt95 = new DVHPoint(); 

        if (dvhData == null)
        {
            MessageBox.Show("No hay datos");
        }
        else
            pt95 = dvhData.CurveData.FirstOrDefault(pto => pto.DoseValue.Dose == 2262);
            MessageBox.Show("Dosis de radiación para: " + pt95.Volume.ToString("F4"));
            
            
    }
  }
}
