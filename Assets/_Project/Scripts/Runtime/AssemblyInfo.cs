using System.Runtime.CompilerServices;

// The asset builders — the skin baker, the track material library builder — fill
// these runtime assets in. Their setters stay internal so nothing at run time can
// rewrite a baked asset, and the editor assembly is let in explicitly rather than
// the setters being made public and merely documented as editor-only.
[assembly: InternalsVisibleTo("OrangeCarrrrr.Editor")]
