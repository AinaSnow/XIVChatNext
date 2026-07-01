using FFXIVClientStructs.FFXIV.Client.Game;

namespace XIVChatPlugin;

/// <summary>
/// Information about a player's current location in a housing ward.
/// </summary>
public class HousingLocation {
    /// <summary>
    /// The housing ward that the player is in.
    /// </summary>
    public ushort? Ward;

    /// <summary>
    /// <para>
    /// The yard that the player is in.
    /// </para>
    /// <para>
    /// This is the same as plot number but indicates that the player is in
    /// the exterior area (the yard) of that plot.
    /// </para>
    /// </summary>
    public ushort? Yard;

    /// <summary>
    /// The plot that the player is in.
    /// </summary>
    public ushort? Plot;

    /// <summary>
    /// The apartment wing (1 or 2 for normal or subdivision) that the
    /// player is in.
    /// </summary>
    public ushort? ApartmentWing;

    /// <summary>
    /// The apartment that the player is in.
    /// </summary>
    public ushort? Apartment;

    internal static unsafe HousingLocation? Current() {
        var manager = HousingManager.Instance();
        return manager == null ? null : new HousingLocation(manager);
    }

    private unsafe HousingLocation(HousingManager* manager) {
        var ward = manager->GetCurrentWard();
        var currentPlot = manager->GetCurrentPlot();
        if (currentPlot < -1) {
            // the struct is in apartment mode
            this.ApartmentWing = (ushort?) ((unchecked((byte) currentPlot) & ~0x80) + 1);
            this.Apartment = (ushort) manager->GetCurrentRoom();
            if (this.Apartment == 0) {
                this.Apartment = null;
            }
        } else if (currentPlot > 0) {
            if (manager->GetCurrentIndoorHouseId().Id == ulong.MaxValue) {
                // not inside a plot
                // yard is 0xFF when not in one
                this.Yard = (ushort?) (currentPlot + 1);
            } else {
                // inside a plot
                this.Plot = (ushort?) (currentPlot + 1);
            }
        }

        if (ward > -1) {
            this.Ward = (ushort) (ward + 1);
        }
    }
}
