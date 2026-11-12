// encodes grab + placed state in a single NetworkSyncedInteger on DreamscapeGrabbable
public static class PlaceableObjectNetworkState
{
    // nobody holding, not placed
    public const int Free = -1;
    // Free to grab, but held at the original pose on every peer until picked up.
    public const int ReturnedHome = -2;

    // placed values live in this low int range so they don't collide with holder hashes
    public const int PlacedBandStart = int.MinValue;
    public const int PlacedBandEnd = int.MinValue + 1_000_000;

    public static bool IsFree(int value) => value == Free || value == ReturnedHome;

    public static bool IsPlaced(int value) => value >= PlacedBandStart && value <= PlacedBandEnd;

    // any other value means a player id hash is holding the object
    public static bool IsHeld(int value) => !IsFree(value) && !IsPlaced(value);

    public static int GetHolderHash(int value) => IsHeld(value) ? value : Free;

    public static int EncodePlaced(string zoneId)
    {
        int key = System.Math.Abs(zoneId.GetHashCode() % 999_999) + 1;
        return PlacedBandStart + key;
    }
}
