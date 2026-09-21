using System.Globalization;
namespace ZeoOreShared {
 internal static class OreSearchHints {
  internal static string MinimumRange(double metres){return "Nearer than "+(metres/1000).ToString("0.#",CultureInfo.InvariantCulture)+" km hidden";}
 }
}
