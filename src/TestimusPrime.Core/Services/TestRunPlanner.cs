using TestimusPrime.Core.Models;

namespace TestimusPrime.Core.Services;

public sealed class TestRunPlanner
{
    public ExecutionSelection Plan(IReadOnlyList<GeneratedTestCase> testCases, TriggerEvent triggerEvent)
    {
        ArgumentNullException.ThrowIfNull(testCases);

        var suites = triggerEvent == TriggerEvent.Commit
            ? new[] { TestSuite.Smoke }
            : new[] { TestSuite.Regression };

        var selected = testCases
            .Where(testCase => testCase.Suites.Any(suites.Contains))
            .ToArray();

        return new ExecutionSelection(triggerEvent, suites, selected);
    }
}
