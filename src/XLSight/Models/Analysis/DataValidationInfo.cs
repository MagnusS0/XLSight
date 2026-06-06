namespace XLSight.Analysis;

/// <summary>Describes a worksheet data-validation rule.</summary>
public sealed class DataValidationInfo
{
    /// <summary>Gets the kind of value accepted by this rule.</summary>
    public required DataValidationType Type { get; init; }

    /// <summary>Gets the original space-separated sequence of worksheet references.</summary>
    public required string SequenceOfReferences { get; init; }

    /// <summary>Gets references parsed as bounded worksheet ranges.</summary>
    public required IReadOnlyList<ExcelRange> Ranges { get; init; }

    /// <summary>Gets the first constraint formula, including a list source for list validation.</summary>
    public string? Formula1 { get; init; }

    /// <summary>Gets the second constraint formula used by between and not-between rules.</summary>
    public string? Formula2 { get; init; }

    /// <summary>Gets the comparison operator, or null when the validation type does not use one.</summary>
    public DataValidationOperator? Operator { get; init; }

    /// <summary>Gets a value indicating whether blank cells are accepted.</summary>
    public required bool AllowBlank { get; init; }

    /// <summary>Gets the OOXML <c>showDropDown</c> flag. Excel interprets true as suppressing the list arrow.</summary>
    public required bool ShowDropDown { get; init; }

    /// <summary>Gets a value indicating whether the input prompt is shown.</summary>
    public required bool ShowInputMessage { get; init; }

    /// <summary>Gets a value indicating whether the error alert is shown.</summary>
    public required bool ShowErrorMessage { get; init; }

    /// <summary>Gets the error-alert style.</summary>
    public required DataValidationErrorStyle ErrorStyle { get; init; }

    /// <summary>Gets the error-alert title.</summary>
    public string? ErrorTitle { get; init; }

    /// <summary>Gets the error-alert message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets the input-prompt title.</summary>
    public string? PromptTitle { get; init; }

    /// <summary>Gets the input-prompt message.</summary>
    public string? PromptMessage { get; init; }
}
