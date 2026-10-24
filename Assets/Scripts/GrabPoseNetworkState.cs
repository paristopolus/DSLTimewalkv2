// Zero means no pose; low byte is 1=left, 2=right; upper bits retain the holder hash.
public static class GrabPoseNetworkState
{
    public static long Encode(int holderHash, int hand)
    {
        return hand == 1 || hand == 2 ? ((long)(uint)holderHash << 8) | (uint)hand : 0L;
    }

    public static int HandForHolder(long value, int holderHash)
    {
        int hand = (int)(value & 255);
        return value != 0 && (uint)(value >> 8) == (uint)holderHash &&
            (hand == 1 || hand == 2) ? hand : 0;
    }
}
