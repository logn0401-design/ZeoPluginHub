using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZeoNav;

internal static partial class Tests
{
    private static void DriveGpsTests()
    {
        var records=File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MainDrives.tsv")).Skip(1).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Split('\t')).ToArray();
        Check("Built-in drive list matches all 31 inspected SDX definitions",records.Length==31 && records.Select(r=>r[0]).Distinct().Count()==MainDriveCatalog.Count);
        foreach(var row in records)
        {
            string id=row[0];
            Check("Exact built-in definition: "+id,MainDriveCatalog.Contains(id) && MainDriveCatalog.Contains(id.ToUpperInvariant()));
            Check("Drive identity survives zero/unknown runtime thrust: "+id,
                DriveClassifier.IsMain("MyObjectBuilder_Thrust/"+id,"",0) && DriveClassifier.IsMain(id,"",double.NaN));
        }
        Check("RCS is not in the main-drive catalog",!MainDriveCatalog.Contains("sdx_thrusterRCSCubeLG") && !DriveClassifier.IsMain("MyObjectBuilder_Thrust/sdx_thrusterRCSCubeLG","RCS",1500000));
        Check("Small non-main thruster remains non-main",!DriveClassifier.IsMain("MyObjectBuilder_Thrust/SmallAtmosphericThruster","Atmospheric",10000));
        var a=new GpsDto {Name="Alpha Station",X=1,Y=2,Z=3,Distance=100};
        var b=new GpsDto {Name="Beta Hauler",X=4,Y=5,Z=6};
        var c=new GpsDto {Name="Alpha Station",X=7,Y=8,Z=9};
        var all=new List<GpsDto> {a,b,c};
        Check("Blank GPS search preserves all destinations and order",GpsSearch.Filter(all,"  ").SequenceEqual(all));
        Check("GPS substring search ignores case",GpsSearch.Filter(all,"pHa st").SequenceEqual(new[]{a,c}));
        Check("GPS terms match in either order",GpsSearch.Filter(all,"station ALPHA").SequenceEqual(new[]{a,c}));
        Check("GPS no-match search is empty",GpsSearch.Filter(all,"missing").Count==0);
        Check("Null GPS list is supported",GpsSearch.Filter(null,"a").Count==0);
        Check("Clearing GPS search restores all options",GpsSearch.Filter(all,"").Count==3);
        Check("Duplicate GPS names retain distinct coordinates",GpsSearch.SelectedIndex(all,c)==2);
        Check("Hidden selection cannot become the first filtered destination",GpsSearch.SelectedIndex(GpsSearch.Filter(all,"beta"),a)==-1);
        Check("GPS refresh keeps selection when only distance changes",GpsSearch.SelectedIndex(all,new GpsDto {Name=a.Name,X=1,Y=2,Z=3,Distance=900})==0);
        Check("GPS refresh detects a moved or renamed destination",!GpsSearch.Same(a,new GpsDto {Name=a.Name,X=2,Y=2,Z=3}) && !GpsSearch.Same(a,new GpsDto {Name="Renamed",X=1,Y=2,Z=3}));
        Check("GPS refresh detects list changes",GpsSearch.SameList(all,new List<GpsDto>(all)) && !GpsSearch.SameList(all,new List<GpsDto>{b,a,c}));
        var sent=new List<NavCommand>(); var host=new NavUiHost {Command=sent.Add};
        host.Select(a); host.Select(null);
        bool rejected=false; try {host.Start();} catch(InvalidOperationException) {rejected=true;}
        Check("Clearing a hidden GPS prevents START and sends no route",rejected && sent.Count==1 && sent[0].Type=="SELECT_GPS");
    }
}
