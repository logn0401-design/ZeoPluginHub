using System;
using System.IO;
namespace ZeosOreOverlay {
 internal static class OverlayLog {
  private static readonly object Gate=new object(); private static DateTime lastError;
  internal static string DirectoryName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Pulsar","ZeosOreHelper");
  internal static void Write(string text){try{lock(Gate){Directory.CreateDirectory(DirectoryName);var p=Path.Combine(DirectoryName,"overlay.log");if(File.Exists(p)&&new FileInfo(p).Length>2*1024*1024)File.Copy(p,p+".previous",true);if(File.Exists(p)&&new FileInfo(p).Length>2*1024*1024)File.WriteAllText(p,"");File.AppendAllText(p,DateTime.UtcNow.ToString("o")+" "+text+Environment.NewLine);}}catch{}}
  internal static void Error(Exception ex){if((DateTime.UtcNow-lastError).TotalSeconds<5)return;lastError=DateTime.UtcNow;Write(ex.ToString());}
 }
}
