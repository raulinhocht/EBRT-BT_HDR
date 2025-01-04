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
        //CODE 2 - Dosis a Volumen (absoluto) BT ------------------------------------------------------------------------------------------

        //Structure PTV = context.StructureSet.Structures.FirstOrDefault(x => x.Id == "PTV_CMI");
        Structure CTV = context.StructureSet.Structures.FirstOrDefault(y => y.Id == "HR-CTV");
        double doseBT = 1614;

        //DVHData dvhData_EBRT = context.PlanSetup.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
        DVHData dvhData_BT = context.PlanSetup.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

        //DVHPoint pt95_EBRT = new DVHPoint();
        DVHPoint pt95_BT = new DVHPoint();

        if (dvhData_BT == null)
        {
            MessageBox.Show("No hay datos");
        }
        else
            //pt95_EBRT = dvhData_EBRT.CurveData.FirstOrDefault(pto => pto.DoseValue.Dose == 2680); //V100
            pt95_BT = dvhData_BT.CurveData.FirstOrDefault(pto => pto.DoseValue.Dose == doseBT); //V10

        MessageBox.Show($"Volumen BT " + pt95_BT.Volume.ToString("F3"));
        }
  }
}
