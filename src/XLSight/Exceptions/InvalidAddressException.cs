namespace XLSight.Exceptions;

/// <summary>Thrown when an address or range string cannot be parsed or exceeds Excel limits.</summary>
public sealed class InvalidAddressException : ExcelException
{
    /// <summary>The address string that failed validation.</summary>
    public string Address { get; }

    /// <summary>Initializes a new instance for the specified invalid address.</summary>
    /// <param name="address">The address string that failed validation.</param>
    public InvalidAddressException(string address)
        : base($"'{address}' is not a valid Excel address or range.")
        => Address = address;

    /// <summary>Initializes a new instance for the specified invalid address with a reason.</summary>
    /// <param name="address">The address string that failed validation.</param>
    /// <param name="reason">A description of why the address is invalid.</param>
    public InvalidAddressException(string address, string reason)
        : base($"'{address}' is not a valid Excel address or range: {reason}.")
        => Address = address;
}
