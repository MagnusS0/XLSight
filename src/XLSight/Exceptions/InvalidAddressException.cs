namespace XLSight.Exceptions;

/// <summary>Thrown when an address or range string cannot be parsed or exceeds Excel limits.</summary>
public sealed class InvalidAddressException : ExcelException
{
    /// <summary>The address string that failed validation.</summary>
    public string Address { get; }

    public InvalidAddressException(string address)
        : base($"'{address}' is not a valid Excel address or range.")
        => Address = address;

    public InvalidAddressException(string address, string reason)
        : base($"'{address}' is not a valid Excel address or range: {reason}.")
        => Address = address;
}
