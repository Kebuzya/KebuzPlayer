using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer;

public enum VizPlacement { Library, Tab, Overlay, HomeWidget }

public static class VizPlacements
{
    public static VizFormFactors FormFactorOf(VizPlacement placement) => placement switch
    {
        VizPlacement.Library    => VizFormFactors.WideStrip,
        VizPlacement.Overlay    => VizFormFactors.Compact,
        VizPlacement.HomeWidget => VizFormFactors.WideStrip,
        _                       => VizFormFactors.Full,
    };
}
