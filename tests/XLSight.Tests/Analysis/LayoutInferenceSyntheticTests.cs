using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Analysis;

/// <summary>Small in-memory workbooks proving layout-inference behaviors that would otherwise
/// only be exercised by the external corpora in <see cref="LayoutInferenceIntegrationTests"/>.</summary>
public sealed class LayoutInferenceSyntheticTests
{
    private const string EmptySstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="0"></sst>
        """;

    // Two stacked statements, each with its own reprinted year header, separated by a lone
    // title row: rows 1-5 are "Income statement", rows 7-11 are "Balance sheet".
    private const string StackedSectionsSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="8">
          <si><t>Income statement</t></si>
          <si><t>Revenue</t></si>
          <si><t>Costs</t></si>
          <si><t>EBITDA</t></si>
          <si><t>Balance sheet</t></si>
          <si><t>Assets</t></si>
          <si><t>Liabilities</t></si>
          <si><t>Equity</t></si>
        </sst>
        """;

    private const string StackedSectionsSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1"><c r="A1" t="s"><v>0</v></c></row>
            <row r="2">
              <c r="B2"><v>2023</v></c>
              <c r="C2"><v>2024</v></c>
              <c r="D2"><v>2025</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>1</v></c>
              <c r="B3"><v>100</v></c>
              <c r="C3"><v>110</v></c>
              <c r="D3"><v>120</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>2</v></c>
              <c r="B4"><v>40</v></c>
              <c r="C4"><v>45</v></c>
              <c r="D4"><v>50</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>3</v></c>
              <c r="B5"><v>60</v></c>
              <c r="C5"><v>65</v></c>
              <c r="D5"><v>70</v></c>
            </row>
            <row r="7"><c r="A7" t="s"><v>4</v></c></row>
            <row r="8">
              <c r="B8"><v>2023</v></c>
              <c r="C8"><v>2024</v></c>
              <c r="D8"><v>2025</v></c>
            </row>
            <row r="9">
              <c r="A9" t="s"><v>5</v></c>
              <c r="B9"><v>500</v></c>
              <c r="C9"><v>520</v></c>
              <c r="D9"><v>540</v></c>
            </row>
            <row r="10">
              <c r="A10" t="s"><v>6</v></c>
              <c r="B10"><v>300</v></c>
              <c r="C10"><v>310</v></c>
              <c r="D10"><v>320</v></c>
            </row>
            <row r="11">
              <c r="A11" t="s"><v>7</v></c>
              <c r="B11"><v>200</v></c>
              <c r="C11"><v>210</v></c>
              <c r="D11"><v>220</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // One table (A2:D5) sits beside a CAGR/Avg block (F2:G5) across an empty spacer column E;
    // both share row labels in column A.
    private const string SiblingFieldsSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="6">
          <si><t>Revenue</t></si>
          <si><t>Costs</t></si>
          <si><t>EBITDA</t></si>
          <si><t>NetIncome</t></si>
          <si><t>CAGR</t></si>
          <si><t>Avg</t></si>
        </sst>
        """;

    private const string SiblingFieldsSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="B1"><v>2023</v></c>
              <c r="C1"><v>2024</v></c>
              <c r="D1"><v>2025</v></c>
              <c r="F1" t="s"><v>4</v></c>
              <c r="G1" t="s"><v>5</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>0</v></c>
              <c r="B2"><v>100</v></c>
              <c r="C2"><v>110</v></c>
              <c r="D2"><v>120</v></c>
              <c r="F2"><v>0.1</v></c>
              <c r="G2"><v>105</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>1</v></c>
              <c r="B3"><v>40</v></c>
              <c r="C3"><v>45</v></c>
              <c r="D3"><v>50</v></c>
              <c r="F3"><v>0.05</v></c>
              <c r="G3"><v>45</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>2</v></c>
              <c r="B4"><v>60</v></c>
              <c r="C4"><v>65</v></c>
              <c r="D4"><v>70</v></c>
              <c r="F4"><v>0.08</v></c>
              <c r="G4"><v>65</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>3</v></c>
              <c r="B5"><v>50</v></c>
              <c r="C5"><v>55</v></c>
              <c r="D5"><v>60</v></c>
              <c r="F5"><v>0.1</v></c>
              <c r="G5"><v>57</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // A dense data block (B3:F7) with non-uniform, sign-alternating deltas (so it can never seed
    // a spurious matrix run itself), flanked by a uniform-step numeric header row (B2:F2) and a
    // uniform-step numeric coordinate column (A3:A7).
    private const string NumericMatrixSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="2">
              <c r="B2"><v>0.01</v></c>
              <c r="C2"><v>0.015</v></c>
              <c r="D2"><v>0.02</v></c>
              <c r="E2"><v>0.025</v></c>
              <c r="F2"><v>0.03</v></c>
            </row>
            <row r="3">
              <c r="A3"><v>0.04</v></c>
              <c r="B3"><v>950</v></c>
              <c r="C3"><v>53</v></c>
              <c r="D3"><v>956</v></c>
              <c r="E3"><v>59</v></c>
              <c r="F3"><v>962</v></c>
            </row>
            <row r="4">
              <c r="A4"><v>0.05</v></c>
              <c r="B4"><v>60</v></c>
              <c r="C4"><v>963</v></c>
              <c r="D4"><v>66</v></c>
              <c r="E4"><v>969</v></c>
              <c r="F4"><v>72</v></c>
            </row>
            <row r="5">
              <c r="A5"><v>0.06</v></c>
              <c r="B5"><v>970</v></c>
              <c r="C5"><v>73</v></c>
              <c r="D5"><v>976</v></c>
              <c r="E5"><v>79</v></c>
              <c r="F5"><v>982</v></c>
            </row>
            <row r="6">
              <c r="A6"><v>0.07</v></c>
              <c r="B6"><v>80</v></c>
              <c r="C6"><v>983</v></c>
              <c r="D6"><v>86</v></c>
              <c r="E6"><v>989</v></c>
              <c r="F6"><v>92</v></c>
            </row>
            <row r="7">
              <c r="A7"><v>0.08</v></c>
              <c r="B7"><v>990</v></c>
              <c r="C7"><v>93</v></c>
              <c r="D7"><v>996</v></c>
              <c r="E7"><v>99</v></c>
              <c r="F7"><v>1002</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // A "Year" column (B) sits between the row-label column (A) and the measure columns
    // (C:D); it repeats 2020-2022 per name and must peel off as a context axis rather than
    // widen the measure field.
    private const string LeadingYearColumnSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="6">
          <si><t>Name</t></si>
          <si><t>Year</t></si>
          <si><t>Assets</t></si>
          <si><t>Deposits</t></si>
          <si><t>Bank A</t></si>
          <si><t>Bank B</t></si>
        </sst>
        """;

    private const string LeadingYearColumnSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" t="s"><v>0</v></c>
              <c r="B1" t="s"><v>1</v></c>
              <c r="C1" t="s"><v>2</v></c>
              <c r="D1" t="s"><v>3</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>4</v></c>
              <c r="B2"><v>2020</v></c>
              <c r="C2"><v>100</v></c>
              <c r="D2"><v>50</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>4</v></c>
              <c r="B3"><v>2021</v></c>
              <c r="C3"><v>110</v></c>
              <c r="D3"><v>55</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>4</v></c>
              <c r="B4"><v>2022</v></c>
              <c r="C4"><v>120</v></c>
              <c r="D4"><v>60</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>5</v></c>
              <c r="B5"><v>2020</v></c>
              <c r="C5"><v>200</v></c>
              <c r="D5"><v>90</v></c>
            </row>
            <row r="6">
              <c r="A6" t="s"><v>5</v></c>
              <c r="B6"><v>2021</v></c>
              <c r="C6"><v>210</v></c>
              <c r="D6"><v>95</v></c>
            </row>
            <row r="7">
              <c r="A7" t="s"><v>5</v></c>
              <c r="B7"><v>2022</v></c>
              <c r="C7"><v>220</v></c>
              <c r="D7"><v>100</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // Day-of-week text header over day numbers: a strictly uniform first data row (1..7) must
    // not be mistaken for a sensitivity matrix's numeric coordinate run.
    private const string CalendarGridSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="7">
          <si><t>Mon</t></si>
          <si><t>Tue</t></si>
          <si><t>Wed</t></si>
          <si><t>Thu</t></si>
          <si><t>Fri</t></si>
          <si><t>Sat</t></si>
          <si><t>Sun</t></si>
        </sst>
        """;

    private const string CalendarGridSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c>
              <c r="D1" t="s"><v>3</v></c><c r="E1" t="s"><v>4</v></c><c r="F1" t="s"><v>5</v></c><c r="G1" t="s"><v>6</v></c>
            </row>
            <row r="2">
              <c r="A2"><v>1</v></c><c r="B2"><v>2</v></c><c r="C2"><v>3</v></c>
              <c r="D2"><v>4</v></c><c r="E2"><v>5</v></c><c r="F2"><v>6</v></c><c r="G2"><v>7</v></c>
            </row>
            <row r="3">
              <c r="A3"><v>8</v></c><c r="B3"><v>9</v></c><c r="C3"><v>10</v></c>
              <c r="D3"><v>11</v></c><c r="E3"><v>12</v></c><c r="F3"><v>13</v></c><c r="G3"><v>14</v></c>
            </row>
            <row r="4">
              <c r="A4"><v>15</v></c><c r="B4"><v>16</v></c><c r="C4"><v>17</v></c>
              <c r="D4"><v>18</v></c><c r="E4"><v>19</v></c><c r="F4"><v>20</v></c><c r="G4"><v>21</v></c>
            </row>
            <row r="5">
              <c r="A5"><v>22</v></c><c r="B5"><v>23</v></c><c r="C5"><v>24</v></c>
              <c r="D5"><v>25</v></c><c r="E5"><v>26</v></c><c r="F5"><v>27</v></c><c r="G5"><v>28</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // A "Quantity" text header with a uniform qty column (10/20/30/40) and a uniform first
    // price row (5.0/5.5/6.0): both uniform runs must not seed a phantom matrix that swallows
    // the text header.
    private const string PricingTableSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="4">
          <si><t>Quantity</t></si>
          <si><t>North</t></si>
          <si><t>South</t></si>
          <si><t>East</t></si>
        </sst>
        """;

    private const string PricingTableSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c><c r="D1" t="s"><v>3</v></c>
            </row>
            <row r="2">
              <c r="A2"><v>10</v></c><c r="B2"><v>5.0</v></c><c r="C2"><v>5.5</v></c><c r="D2"><v>6.0</v></c>
            </row>
            <row r="3">
              <c r="A3"><v>20</v></c><c r="B3"><v>4.8</v></c><c r="C3"><v>5.9</v></c><c r="D3"><v>6.4</v></c>
            </row>
            <row r="4">
              <c r="A4"><v>30</v></c><c r="B4"><v>4.1</v></c><c r="C4"><v>5.2</v></c><c r="D4"><v>6.9</v></c>
            </row>
            <row r="5">
              <c r="A5"><v>40</v></c><c r="B5"><v>3.9</v></c><c r="C5"><v>4.8</v></c><c r="D5"><v>7.7</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // Row 1 reprints band labels ("Group A" x2, "Group B" x2), row 2 reprints years under them;
    // the year row must not be swallowed as measure data by the band header's own candidate.
    private const string BandOverYearHeaderSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="5">
          <si><t>Group A</t></si>
          <si><t>Group B</t></si>
          <si><t>Revenue</t></si>
          <si><t>Costs</t></si>
          <si><t>EBITDA</t></si>
        </sst>
        """;

    private const string BandOverYearHeaderSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="B1" t="s"><v>0</v></c><c r="C1" t="s"><v>0</v></c><c r="D1" t="s"><v>1</v></c><c r="E1" t="s"><v>1</v></c>
            </row>
            <row r="2">
              <c r="B2"><v>2023</v></c><c r="C2"><v>2024</v></c><c r="D2"><v>2023</v></c><c r="E2"><v>2024</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>2</v></c><c r="B3"><v>10</v></c><c r="C3"><v>13</v></c><c r="D3"><v>12</v></c><c r="E3"><v>18</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>3</v></c><c r="B4"><v>20</v></c><c r="C4"><v>27</v></c><c r="D4"><v>22</v></c><c r="E4"><v>31</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>4</v></c><c r="B5"><v>34</v></c><c r="C5"><v>41</v></c><c r="D5"><v>39</v></c><c r="E5"><v>52</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // A "Units" data column (B) holds non-monotonic integers that happen to fall in the year
    // range (1950/2010/1980); unlike a real year key, it must stay in the measure field.
    private const string NonMonotonicUnitsColumnSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="7">
          <si><t>Product</t></si>
          <si><t>Units</t></si>
          <si><t>Price</t></si>
          <si><t>Total</t></si>
          <si><t>Alpha</t></si>
          <si><t>Beta</t></si>
          <si><t>Gamma</t></si>
        </sst>
        """;

    private const string NonMonotonicUnitsColumnSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c><c r="D1" t="s"><v>3</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>4</v></c><c r="B2"><v>1950</v></c><c r="C2"><v>5</v></c><c r="D2"><v>9750</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>5</v></c><c r="B3"><v>2010</v></c><c r="C3"><v>7</v></c><c r="D3"><v>14070</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>6</v></c><c r="B4"><v>1980</v></c><c r="C4"><v>6</v></c><c r="D4"><v>11880</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // A uniform-stepped forecast row (row 4, mimicking CAGR-driven model output that happens to
    // step near-uniformly) sits flush under two ordinary data rows, and the column just left of
    // its run (C) also steps near-uniformly for 3 rows starting below it — the same shape that
    // seeds a phantom sensitivity matrix. Unlike a standalone matrix, there is no title or blank
    // row between this "coordinate" run and the table's own data directly above it.
    private const string EmbeddedForecastRowSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="8">
          <si><t>Name</t></si>
          <si><t>Year</t></si>
          <si><t>Metric0</t></si>
          <si><t>Metric1</t></si>
          <si><t>Metric2</t></si>
          <si><t>Metric3</t></si>
          <si><t>Metric4</t></si>
          <si><t>Foo</t></si>
        </sst>
        """;

    private const string EmbeddedForecastRowSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c>
              <c r="D1" t="s"><v>3</v></c><c r="E1" t="s"><v>4</v></c><c r="F1" t="s"><v>5</v></c><c r="G1" t="s"><v>6</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>7</v></c><c r="B2"><v>2020</v></c><c r="C2"><v>200</v></c>
              <c r="D2"><v>10</v></c><c r="E2"><v>20</v></c><c r="F2"><v>30</v></c><c r="G2"><v>40</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>7</v></c><c r="B3"><v>2021</v></c><c r="C3"><v>210</v></c>
              <c r="D3"><v>41</v></c><c r="E3"><v>42</v></c><c r="F3"><v>43</v></c><c r="G3"><v>44</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>7</v></c><c r="B4"><v>2022</v></c><c r="C4"><v>463</v></c>
              <c r="D4"><v>590.4</v></c><c r="E4"><v>670.9</v></c><c r="F4"><v>752.2</v></c><c r="G4"><v>834.9</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>7</v></c><c r="B5"><v>2023</v></c><c r="C5"><v>463</v></c>
              <c r="D5"><v>111</v></c><c r="E5"><v>112</v></c><c r="F5"><v>113</v></c><c r="G5"><v>114</v></c>
            </row>
            <row r="6">
              <c r="A6" t="s"><v>7</v></c><c r="B6"><v>2024</v></c><c r="C6"><v>1620</v></c>
              <c r="D6"><v>121</v></c><c r="E6"><v>122</v></c><c r="F6"><v>123</v></c><c r="G6"><v>124</v></c>
            </row>
            <row r="7">
              <c r="A7" t="s"><v>7</v></c><c r="B7"><v>2025</v></c><c r="C7"><v>2786</v></c>
              <c r="D7"><v>131</v></c><c r="E7"><v>132</v></c><c r="F7"><v>133</v></c><c r="G7"><v>134</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // A lone caption cell ("Growth (%)") sits directly above a text-kind header row (Q1/Q2/Q3);
    // the header's own cells are plain text, not numeric/date, so only a horizontal axis's
    // always-probe rule (not the value-kind gate) picks up the caption as a title.
    private const string CaptionedTextHeaderSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="8">
          <si><t>Growth (%)</t></si>
          <si><t>Name</t></si>
          <si><t>Q1</t></si>
          <si><t>Q2</t></si>
          <si><t>Q3</t></si>
          <si><t>Foo</t></si>
          <si><t>Bar</t></si>
          <si><t>Baz</t></si>
        </sst>
        """;

    private const string CaptionedTextHeaderSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1"><c r="B1" t="s"><v>0</v></c></row>
            <row r="2">
              <c r="A2" t="s"><v>1</v></c><c r="B2" t="s"><v>2</v></c><c r="C2" t="s"><v>3</v></c><c r="D2" t="s"><v>4</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>5</v></c><c r="B3"><v>10</v></c><c r="C3"><v>11</v></c><c r="D3"><v>12</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>6</v></c><c r="B4"><v>20</v></c><c r="C4"><v>21</v></c><c r="D4"><v>22</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>7</v></c><c r="B5"><v>30</v></c><c r="C5"><v>31</v></c><c r="D5"><v>32</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // Column A carries row labels for two sections; rows 2 and 6 are section headers with no
    // data in B:C ("Funding" and "Loans"), each followed by their labeled data rows.
    private const string AxisSectionsSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="8">
          <si><t>Funding</t></si>
          <si><t>Deposits</t></si>
          <si><t>Savings</t></si>
          <si><t>Total Funding</t></si>
          <si><t>Loans</t></si>
          <si><t>Mortgages</t></si>
          <si><t>Auto</t></si>
          <si><t>Total Loans</t></si>
        </sst>
        """;

    private const string AxisSectionsSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="B1"><v>2023</v></c>
              <c r="C1"><v>2024</v></c>
            </row>
            <row r="2"><c r="A2" t="s"><v>0</v></c></row>
            <row r="3">
              <c r="A3" t="s"><v>1</v></c>
              <c r="B3"><v>100</v></c>
              <c r="C3"><v>110</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>2</v></c>
              <c r="B4"><v>200</v></c>
              <c r="C4"><v>210</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>3</v></c>
              <c r="B5"><v>300</v></c>
              <c r="C5"><v>320</v></c>
            </row>
            <row r="6"><c r="A6" t="s"><v>4</v></c></row>
            <row r="7">
              <c r="A7" t="s"><v>5</v></c>
              <c r="B7"><v>150</v></c>
              <c r="C7"><v>160</v></c>
            </row>
            <row r="8">
              <c r="A8" t="s"><v>6</v></c>
              <c r="B8"><v>90</v></c>
              <c r="C8"><v>95</v></c>
            </row>
            <row r="9">
              <c r="A9" t="s"><v>7</v></c>
              <c r="B9"><v>240</v></c>
              <c r="C9"><v>255</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // The right-hand metric block runs two rows longer than the main table. Dense siblings use
    // the leftmost table as the row-span anchor, so the extra right-side rows remain outside it.
    private const string SiblingRowsLeftAnchorSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="8">
          <si><t>Revenue</t></si>
          <si><t>Costs</t></si>
          <si><t>EBITDA</t></si>
          <si><t>Other income</t></si>
          <si><t>Other costs</t></si>
          <si><t>CAGR</t></si>
          <si><t>Avg</t></si>
          <si><t>Metric</t></si>
        </sst>
        """;

    private const string SiblingRowsLeftAnchorSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="B1"><v>2023</v></c>
              <c r="C1"><v>2024</v></c>
              <c r="E1" t="s"><v>5</v></c>
              <c r="F1" t="s"><v>6</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>0</v></c>
              <c r="B2"><v>100</v></c>
              <c r="C2"><v>110</v></c>
              <c r="E2"><v>0.1</v></c>
              <c r="F2"><v>105</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>1</v></c>
              <c r="B3"><v>40</v></c>
              <c r="C3"><v>44</v></c>
              <c r="E3"><v>0.1</v></c>
              <c r="F3"><v>42</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>2</v></c>
              <c r="B4"><v>60</v></c>
              <c r="C4"><v>66</v></c>
              <c r="E4"><v>0.1</v></c>
              <c r="F4"><v>63</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>3</v></c>
              <c r="E5"><v>5</v></c>
              <c r="F5"><v>6</v></c>
            </row>
            <row r="6">
              <c r="A6" t="s"><v>4</v></c>
              <c r="E6"><v>7</v></c>
              <c r="F6"><v>8</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // Two co-extensive panels sit on either side of a shorter table in the gap. The side panels
    // must not merge across that different-span table, because that would create overlapping fields.
    private const string InterveningTableSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="9">
          <si><t>Revenue</t></si>
          <si><t>Costs</t></si>
          <si><t>EBITDA</t></si>
          <si><t>Tax</t></si>
          <si><t>Cash</t></si>
          <si><t>Current</t></si>
          <si><t>Prior</t></si>
          <si><t>Short A</t></si>
          <si><t>Short B</t></si>
        </sst>
        """;

    private const string InterveningTableSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="B1"><v>2023</v></c>
              <c r="C1"><v>2024</v></c>
              <c r="F1" t="s"><v>5</v></c>
              <c r="G1" t="s"><v>6</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>0</v></c>
              <c r="B2"><v>100</v></c>
              <c r="C2"><v>110</v></c>
              <c r="F2"><v>10</v></c>
              <c r="G2"><v>11</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>1</v></c>
              <c r="B3"><v>40</v></c>
              <c r="C3"><v>44</v></c>
              <c r="D3" t="s"><v>7</v></c>
              <c r="E3" t="s"><v>8</v></c>
              <c r="F3"><v>20</v></c>
              <c r="G3"><v>22</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>2</v></c>
              <c r="B4"><v>60</v></c>
              <c r="C4"><v>66</v></c>
              <c r="D4"><v>1</v></c>
              <c r="E4"><v>2</v></c>
              <c r="F4"><v>30</v></c>
              <c r="G4"><v>33</v></c>
            </row>
            <row r="5">
              <c r="A5" t="s"><v>3</v></c>
              <c r="B5"><v>25</v></c>
              <c r="C5"><v>28</v></c>
              <c r="D5"><v>3</v></c>
              <c r="E5"><v>4</v></c>
              <c r="F5"><v>40</v></c>
              <c r="G5"><v>44</v></c>
            </row>
            <row r="6">
              <c r="A6" t="s"><v>4</v></c>
              <c r="B6"><v>35</v></c>
              <c r="C6"><v>38</v></c>
              <c r="F6"><v>50</v></c>
              <c r="G6"><v>55</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // Same shape as NumericMatrixSheetXml, but C3 is a value-less formula (no cached <v>, as
    // openpyxl always writes formulas) instead of a plain number. The scanner sees an empty
    // cell at C3 and must not carry the pending formula flag onto D3.
    private const string ValuelessFormulaSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="2">
              <c r="B2"><v>0.01</v></c>
              <c r="C2"><v>0.015</v></c>
              <c r="D2"><v>0.02</v></c>
              <c r="E2"><v>0.025</v></c>
              <c r="F2"><v>0.03</v></c>
            </row>
            <row r="3">
              <c r="A3"><v>0.04</v></c>
              <c r="B3"><v>950</v></c>
              <c r="C3"><f>B3*2</f></c>
              <c r="D3"><v>956</v></c>
              <c r="E3"><v>59</v></c>
              <c r="F3"><v>962</v></c>
            </row>
            <row r="4">
              <c r="A4"><v>0.05</v></c>
              <c r="B4"><v>60</v></c>
              <c r="C4"><v>963</v></c>
              <c r="D4"><v>66</v></c>
              <c r="E4"><v>969</v></c>
              <c r="F4"><v>72</v></c>
            </row>
            <row r="5">
              <c r="A5"><v>0.06</v></c>
              <c r="B5"><v>970</v></c>
              <c r="C5"><v>73</v></c>
              <c r="D5"><v>976</v></c>
              <c r="E5"><v>79</v></c>
              <c r="F5"><v>982</v></c>
            </row>
            <row r="6">
              <c r="A6"><v>0.07</v></c>
              <c r="B6"><v>80</v></c>
              <c r="C6"><v>983</v></c>
              <c r="D6"><v>86</v></c>
              <c r="E6"><v>989</v></c>
              <c r="F6"><v>92</v></c>
            </row>
            <row r="7">
              <c r="A7"><v>0.08</v></c>
              <c r="B7"><v>990</v></c>
              <c r="C7"><v>93</v></c>
              <c r="D7"><v>996</v></c>
              <c r="E7"><v>99</v></c>
              <c r="F7"><v>1002</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    [Fact]
    public void StackedSections_SplitAtReprintedHeaders_WithGroupTitles()
    {
        SheetLayoutInfo layout = Infer(StackedSectionsSheetXml, StackedSectionsSstXml);

        AssertField(layout, "B3:D5", 2);
        AssertField(layout, "B9:D11", 2);
        AssertAxis(layout, "A3:A5", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        AssertAxis(layout, "A9:A11", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        AssertAxis(layout, "B2:D2", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        AssertAxis(layout, "B8:D8", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);

        Assert.Equal(["Income statement", "Balance sheet"], layout.Groups.Select(static group => group.Title));
    }

    [Fact]
    public void SiblingFields_ShareRowLabelAxis_AcrossEmptySpacer()
    {
        SheetLayoutInfo layout = Infer(SiblingFieldsSheetXml, SiblingFieldsSstXml);

        MeasureFieldInfo left = AssertField(layout, "B2:D5", 2);
        MeasureFieldInfo right = AssertField(layout, "F2:G5", 2);
        LayoutAxis labelAxis = AssertAxis(layout, "A2:A5", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);

        Assert.Contains(labelAxis.Id, left.AxisIds);
        Assert.Contains(labelAxis.Id, right.AxisIds);
    }

    [Fact]
    public void NumericCoordinateMatrix_GetsOwnAxes()
    {
        SheetLayoutInfo layout = Infer(NumericMatrixSheetXml, EmptySstXml);

        MeasureFieldInfo matrix = AssertField(layout, "B3:F7", 2);
        LayoutAxis waccAxis = AssertAxis(layout, "A3:A7", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        LayoutAxis growthAxis = AssertAxis(layout, "B2:F2", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Numeric, waccAxis.ValueKind);
        Assert.Equal(LayoutAxisValueKind.Numeric, growthAxis.ValueKind);
        Assert.Contains(waccAxis.Id, matrix.AxisIds);
        Assert.Contains(growthAxis.Id, matrix.AxisIds);
    }

    [Fact]
    public void ValuelessFormulaCell_DoesNotMarkNextCellAsFormula()
    {
        SheetLayoutInfo layout = Infer(ValuelessFormulaSheetXml, EmptySstXml);

        MeasureFieldInfo field = AssertField(layout, "B3:F7", 2);
        Assert.Equal(0, field.Profile.FormulaCount);
    }

    [Fact]
    public void LeadingYearColumn_PeelsIntoContextAxis()
    {
        SheetLayoutInfo layout = Infer(LeadingYearColumnSheetXml, LeadingYearColumnSstXml);

        MeasureFieldInfo field = AssertField(layout, "C2:D7", 3);
        LayoutAxis primary = AssertAxis(layout, "A2:A7", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        LayoutAxis context = AssertAxis(layout, "B2:B7", LayoutAxisOrientation.Vertical, LayoutAxisRole.Context);
        Assert.Contains(primary.Id, field.AxisIds);
        Assert.Contains(context.Id, field.AxisIds);
    }

    [Fact]
    public void CalendarGrid_DoesNotBecomePhantomMatrix()
    {
        SheetLayoutInfo layout = Infer(CalendarGridSheetXml, CalendarGridSstXml);

        // The whole grid (day numbers included) stays one dense field under the day-name text
        // header; no B3:G5 (or any other) matrix carves the day numbers out as coordinates.
        MeasureFieldInfo field = AssertField(layout, "A2:G5", 1);
        LayoutAxis dayNames = AssertAxis(layout, "A1:G1", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Text, dayNames.ValueKind);
        Assert.Contains(dayNames.Id, field.AxisIds);
        Assert.DoesNotContain(layout.MeasureFields, static f => f.Range == ExcelRange.Parse("B3:G5"));
    }

    [Fact]
    public void PricingTable_UniformFirstRowDoesNotEatTextHeader()
    {
        SheetLayoutInfo layout = Infer(PricingTableSheetXml, PricingTableSstXml);

        // The text header row (Quantity, North, South, East) survives intact over the whole
        // table, including the uniform Quantity column; no B3:D5 matrix eats it.
        MeasureFieldInfo field = AssertField(layout, "A2:D5", 1);
        LayoutAxis header = AssertAxis(layout, "A1:D1", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Text, header.ValueKind);
        Assert.Contains(header.Id, field.AxisIds);
        Assert.DoesNotContain(layout.MeasureFields, static f => f.Range == ExcelRange.Parse("B3:D5"));
    }

    [Fact]
    public void BandOverYearHeader_YearRowStaysOutOfMeasureField()
    {
        SheetLayoutInfo layout = Infer(BandOverYearHeaderSheetXml, BandOverYearHeaderSstXml);

        MeasureFieldInfo field = AssertField(layout, "B3:E5", 2);
        LayoutAxis years = AssertAxis(layout, "B2:E2", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        LayoutAxis labels = AssertAxis(layout, "A3:A5", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Numeric, years.ValueKind);
        Assert.Contains(years.Id, field.AxisIds);
        Assert.Contains(labels.Id, field.AxisIds);
    }

    [Fact]
    public void NonMonotonicIntegerColumn_StaysInMeasureField()
    {
        SheetLayoutInfo layout = Infer(NonMonotonicUnitsColumnSheetXml, NonMonotonicUnitsColumnSstXml);

        AssertField(layout, "B2:D4", 2);
        Assert.DoesNotContain(
            layout.Axes,
            static axis => axis.Range == ExcelRange.Parse("B2:B4") &&
                (axis.Orientation == LayoutAxisOrientation.Vertical || axis.Role == LayoutAxisRole.Context));
    }

    [Fact]
    public void EmbeddedForecastRow_DoesNotSeedPhantomMatrix()
    {
        SheetLayoutInfo layout = Infer(EmbeddedForecastRowSheetXml, EmbeddedForecastRowSstXml);

        // The whole table stays one field spanning all six data rows; the uniform-stepped
        // forecast row (4) and the uniform-stepped coordinate-look-alike column below it (C5:C7)
        // must not carve out a separate D5:G7 matrix.
        AssertField(layout, "C2:G7", 3);
        Assert.DoesNotContain(layout.MeasureFields, static f => f.Range == ExcelRange.Parse("D5:G7"));
        Assert.Single(layout.MeasureFields);
    }

    [Fact]
    public void HorizontalTextAxis_PicksUpLoneCaptionCellAbove()
    {
        SheetLayoutInfo layout = Infer(CaptionedTextHeaderSheetXml, CaptionedTextHeaderSstXml);

        LayoutAxis header = AssertAxis(layout, "B2:D2", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Text, header.ValueKind);
        Assert.Equal("Growth (%)", header.Title);
    }

    [Fact]
    public void AxisSections_FromNoDataHeaderRows()
    {
        SheetLayoutInfo layout = Infer(AxisSectionsSheetXml, AxisSectionsSstXml);

        LayoutAxis labelAxis = AssertAxis(layout, "A3:A9", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Contains(labelAxis.Sections, static section =>
            string.Equals(section.Title, "Funding", StringComparison.Ordinal) && section.Range == ExcelRange.Parse("A2:A5"));
        Assert.Contains(labelAxis.Sections, static section =>
            string.Equals(section.Title, "Loans", StringComparison.Ordinal) && section.Range == ExcelRange.Parse("A6:A9"));
    }

    [Fact]
    public void SiblingRows_UseLeftmostFieldAsRowSpanAnchor()
    {
        SheetLayoutInfo layout = Infer(SiblingRowsLeftAnchorSheetXml, SiblingRowsLeftAnchorSstXml);

        AssertField(layout, "B2:C4", 2);
        AssertField(layout, "E2:F4", 2);
        Assert.DoesNotContain(layout.MeasureFields, static field => field.Range == ExcelRange.Parse("E2:F6"));
    }

    [Fact]
    public void MergeColumnAdjacentFields_DoesNotMergeAcrossDifferentSpanTable()
    {
        SheetLayoutInfo layout = Infer(InterveningTableSheetXml, InterveningTableSstXml);

        AssertFieldRange(layout, "B2:C6");
        AssertFieldRange(layout, "D4:E5");
        AssertFieldRange(layout, "F2:G6");
        AssertNoOverlappingFields(layout);
    }

    private static SheetLayoutInfo Infer(string sheetXml, string sstXml)
    {
        using var ms = LayoutTestWorkbook.Build(sheetXml, sstXml);
        using var workbook = ExcelWorkbook.Open(ms);
        return Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Data").Inferred).Layout;
    }

    private static MeasureFieldInfo AssertField(SheetLayoutInfo layout, string range, int rank)
    {
        MeasureFieldInfo field = AssertFieldRange(layout, range);
        Assert.Equal(rank, field.Rank);
        return field;
    }

    private static MeasureFieldInfo AssertFieldRange(SheetLayoutInfo layout, string range)
    {
        var expectedRange = ExcelRange.Parse(range);
        MeasureFieldInfo? field = layout.MeasureFields.FirstOrDefault(field => field.Range == expectedRange);
        Assert.NotNull(field);
        return field;
    }

    private static LayoutAxis AssertAxis(
        SheetLayoutInfo layout,
        string range,
        LayoutAxisOrientation orientation,
        LayoutAxisRole role)
    {
        var expectedRange = ExcelRange.Parse(range);
        LayoutAxis? axis = layout.Axes.FirstOrDefault(axis =>
            axis.Range == expectedRange &&
            axis.Orientation == orientation &&
            axis.Role == role);
        Assert.NotNull(axis);
        return axis;
    }

    private static void AssertNoOverlappingFields(SheetLayoutInfo layout)
    {
        for (int i = 0; i < layout.MeasureFields.Count; i++)
        {
            for (int j = i + 1; j < layout.MeasureFields.Count; j++)
            {
                Assert.False(
                    Overlaps(layout.MeasureFields[i].Range, layout.MeasureFields[j].Range),
                    $"{layout.MeasureFields[i].Range} overlaps {layout.MeasureFields[j].Range}");
            }
        }
    }

    private static bool Overlaps(ExcelRange left, ExcelRange right) =>
        Math.Min(left.BottomRight.Column, right.BottomRight.Column) >= Math.Max(left.TopLeft.Column, right.TopLeft.Column) &&
        Math.Min(left.BottomRight.Row, right.BottomRight.Row) >= Math.Max(left.TopLeft.Row, right.TopLeft.Row);
}
