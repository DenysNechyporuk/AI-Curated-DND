using System.Text.RegularExpressions;

namespace AiDnD.Core.Game;

/// <summary>Джерело випадкових чисел. Виділене в інтерфейс, щоб у тестах кидки були детермінованими.</summary>
public interface IRandomSource
{
    /// <returns>Ціле число в діапазоні [min, max] включно.</returns>
    int Next(int minInclusive, int maxInclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;
    public SystemRandomSource(int? seed = null) => _random = seed is null ? Random.Shared : new Random(seed.Value);
    public int Next(int minInclusive, int maxInclusive) => _random.Next(minInclusive, maxInclusive + 1);
}

public sealed record DieRoll(int Sides, int Value);

public sealed record DiceRollResult(string Expression, IReadOnlyList<DieRoll> Rolls, int Modifier, int Total)
{
    public override string ToString()
    {
        var dice = Rolls.Count == 0 ? "" : "[" + string.Join(", ", Rolls.Select(r => r.Value)) + "]";
        var mod = Modifier == 0 ? "" : Modifier > 0 ? $" +{Modifier}" : $" {Modifier}";
        return $"{Expression} → {dice}{mod} = {Total}";
    }
}

/// <summary>
/// Кидає кубики за виразом у нотації D&amp;D: "1d20", "2d6+3", "d8-1", "1d6+1d4+2".
/// </summary>
public sealed partial class DiceRoller
{
    private const int MaxDiceCount = 100;
    private const int MaxSides = 1000;

    private readonly IRandomSource _random;

    public DiceRoller(IRandomSource? random = null) => _random = random ?? new SystemRandomSource();

    [GeneratedRegex(@"^[+-]?(\d*d\d+|\d+)([+-](\d*d\d+|\d+))*$")]
    private static partial Regex ValidExpression();

    [GeneratedRegex(@"([+-]?)(\d*d\d+|\d+)")]
    private static partial Regex Term();

    /// <param name="expression">Вираз кидка.</param>
    /// <param name="diceMultiplier">Множник кількості кубиків (2 для критичного удару).</param>
    public DiceRollResult Roll(string expression, int diceMultiplier = 1)
    {
        var normalized = (expression ?? "").Replace(" ", "").ToLowerInvariant();
        if (normalized.Length == 0 || !ValidExpression().IsMatch(normalized))
            throw new GameRuleException($"Некоректний вираз кубиків: '{expression}'. Приклад: 2d6+3");

        var rolls = new List<DieRoll>();
        int modifier = 0, total = 0;

        foreach (Match m in Term().Matches(normalized))
        {
            int sign = m.Groups[1].Value == "-" ? -1 : 1;
            var body = m.Groups[2].Value;
            var dIndex = body.IndexOf('d');

            if (dIndex < 0)
            {
                var flat = sign * int.Parse(body);
                modifier += flat;
                total += flat;
                continue;
            }

            int count = dIndex == 0 ? 1 : int.Parse(body[..dIndex]);
            int sides = int.Parse(body[(dIndex + 1)..]);
            count *= diceMultiplier;

            if (count < 1 || count > MaxDiceCount) throw new GameRuleException($"Кількість кубиків має бути 1..{MaxDiceCount}.");
            if (sides < 2 || sides > MaxSides) throw new GameRuleException($"Кубик має мати 2..{MaxSides} граней.");

            for (int i = 0; i < count; i++)
            {
                var value = _random.Next(1, sides);
                rolls.Add(new DieRoll(sides, value));
                total += sign * value;
            }
        }

        return new DiceRollResult(normalized, rolls, modifier, total);
    }

    public int RollD20() => _random.Next(1, 20);
}

/// <summary>Порушення правил гри (невідома ціль, мертвий атакуючий тощо). Повідомлення повертається моделі.</summary>
public sealed class GameRuleException(string message) : Exception(message);
