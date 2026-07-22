using Xenaia.Modules.Sync.Availability;

namespace Xenaia.Modules.Sync.Tests.Availability;

public class SheetCombinationParserTests
{
    private readonly SheetCombinationParser _parser = new();

    [Fact]
    public void Two_row_group_under_one_combination_yields_one_combination_with_two_times()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["09:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"],
            ["14:00", "", "", "", ""],
        ];

        var result = _parser.Parse(rows);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(0, result.BadRows);
        var combination = Assert.Single(result.Combinations);
        Assert.Equal(100, combination.ProductExternalId);
        Assert.Equal(7, combination.OptionExternalId);
        Assert.Equal(new DateOnly(2026, 8, 1), combination.From);
        Assert.Equal(new DateOnly(2026, 8, 1), combination.To);
        Assert.Equal(2, combination.Timeslots.Count);
        Assert.Equal(1, combination.Timeslots[0].RowNumber);
        Assert.Equal(new TimeOnly(9, 0), combination.Timeslots[0].Time);
        Assert.Equal(2, combination.Timeslots[1].RowNumber);
        Assert.Equal(new TimeOnly(14, 0), combination.Timeslots[1].Time);
    }

    [Fact]
    public void Blank_column_a_is_the_slotless_sentinel_not_a_parse_failure()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["", "100", "7", "", "100|7|2026-08-01|2026-08-01"],
        ];

        var result = _parser.Parse(rows);

        Assert.Equal(0, result.BadRows);
        var combination = Assert.Single(result.Combinations);
        var timeslot = Assert.Single(combination.Timeslots);
        Assert.Null(timeslot.Time);
    }

    [Fact]
    public void Malformed_combination_string_counts_as_a_bad_row_and_is_reported_not_thrown()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["09:00", "100", "7", "adult", "not-a-valid-combination"],
        ];

        var exception = Record.Exception(() => _parser.Parse(rows));

        Assert.Null(exception);
        var result = _parser.Parse(rows);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.BadRows);
        Assert.Empty(result.Combinations);
    }

    [Fact]
    public void Malformed_combination_does_not_carry_forward_to_later_rows()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["09:00", "100", "7", "adult", "garbage"],
            ["10:00", "", "", "", ""],
            ["11:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"],
        ];

        var result = _parser.Parse(rows);

        Assert.Equal(1, result.BadRows);
        var combination = Assert.Single(result.Combinations);
        var timeslot = Assert.Single(combination.Timeslots);
        Assert.Equal(3, timeslot.RowNumber);
        Assert.Equal(new TimeOnly(11, 0), timeslot.Time);
    }

    [Fact]
    public void Empty_sheet_yields_an_empty_result()
    {
        var result = _parser.Parse([]);

        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.BadRows);
        Assert.Empty(result.Combinations);
    }

    [Fact]
    public void Rows_before_the_first_combination_are_skipped_and_not_reported_as_bad()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["09:00", "", "", "", ""],
            ["", "", "", "", ""],
            ["10:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"],
        ];

        var result = _parser.Parse(rows);

        Assert.Equal(3, result.TotalRows);
        Assert.Equal(0, result.BadRows);
        var combination = Assert.Single(result.Combinations);
        var timeslot = Assert.Single(combination.Timeslots);
        Assert.Equal(3, timeslot.RowNumber);
        Assert.Equal(new TimeOnly(10, 0), timeslot.Time);
    }

    [Fact]
    public void Two_combinations_in_sequence_each_carry_their_own_rows_forward()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["09:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"],
            ["14:00", "", "", "", ""],
            ["09:00", "200", "3", "adult", "200|3|2026-08-02|2026-08-02"],
        ];

        var result = _parser.Parse(rows);

        Assert.Equal(2, result.Combinations.Count);
        Assert.Equal(2, result.Combinations[0].Timeslots.Count);
        Assert.Equal(100, result.Combinations[0].ProductExternalId);
        Assert.Single(result.Combinations[1].Timeslots);
        Assert.Equal(200, result.Combinations[1].ProductExternalId);
        Assert.Equal(new DateOnly(2026, 8, 2), result.Combinations[1].From);
    }

    [Fact]
    public void Row_with_fewer_than_five_cells_is_treated_as_a_blank_combination_cell()
    {
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["09:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"],
            ["14:00"],
        ];

        var result = _parser.Parse(rows);

        Assert.Equal(0, result.BadRows);
        var combination = Assert.Single(result.Combinations);
        Assert.Equal(2, combination.Timeslots.Count);
        Assert.Equal(new TimeOnly(14, 0), combination.Timeslots[1].Time);
    }
}
