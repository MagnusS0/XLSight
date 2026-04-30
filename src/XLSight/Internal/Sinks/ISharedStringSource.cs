namespace XLSight.Internal.Sinks;

internal interface ISharedStringSource
{
    public string GetString(int index);

    public int GetCharCount(int index);
}
