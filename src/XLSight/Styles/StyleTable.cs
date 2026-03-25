namespace XLSight.Styles;

internal sealed class StyleTable
{
    private readonly FormatClass[] _styleClassifications;

    internal static StyleTable Default { get; } = new StyleTable([]);

    internal StyleTable(FormatClass[] classifications)
    {
        _styleClassifications = classifications;
    }

    internal FormatClass GetClassification(int styleIndex)
    {
        if ((uint)styleIndex >= (uint)_styleClassifications.Length)
        {
            return FormatClass.General;
        }
        return _styleClassifications[styleIndex];
    }
}
