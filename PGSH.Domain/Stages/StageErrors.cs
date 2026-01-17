using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public static class StageErrors
{
    // === Stage ===
    public static Error NotFound(int stageId) => Error.NotFound(
        "Stages.NotFound",
        $"The stage with Id = '{stageId}' was not found.");

    public static Error DuplicateName(string name) => Error.Conflict(
        "Stages.DuplicateName",
        $"A stage with the name '{name}' already exists.");

    public static readonly Error InvalidDuration = Error.Validation(
        "Stages.InvalidDuration",
        "The stage duration must be greater than zero.");

    public static readonly Error MissingLevel = Error.Validation(
        "Stages.MissingLevel",
        "A level must be assigned to the stage.");

    public static readonly Error InvalidCoefficient = Error.Validation(
        "Stages.InvalidCoefficient",
        "The stage coefficient must be greater than or equal to 1.");

    // === StageGroup ===
    public static Error GroupNotFound(int groupId) => Error.NotFound(
        "StageGroups.NotFound",
        $"The stage group with Id = '{groupId}' was not found.");

    public static Error DuplicateGroupLabel(string label) => Error.Conflict(
        "StageGroups.DuplicateLabel",
        $"A stage group with the label '{label}' already exists.");

    public static readonly Error MissingStageReference = Error.Validation(
        "StageGroups.MissingStageReference",
        "Each stage group must be associated with a valid stage.");

    public static readonly Error EmptyLabel = Error.Validation(
        "StageGroups.EmptyLabel",
        "Stage group label cannot be null or empty.");

    // === InternshipAssignment ===
    public static Error AssignmentNotFound(Guid assignmentId) => Error.NotFound(
        "InternshipAssignments.NotFound",
        $"The internship assignment with Id = '{assignmentId}' was not found.");

    public static readonly Error InvalidDateRange = Error.Validation(
        "InternshipAssignments.InvalidDateRange",
        "The planned end date must be greater than or equal to the start date.");

    public static readonly Error NegativeScore = Error.Validation(
        "InternshipAssignments.NegativeScore",
        "The internship score cannot be negative.");

    public static readonly Error MissingStageGroup = Error.Validation(
        "InternshipAssignments.MissingStageGroup",
        "An internship assignment must be linked to a valid stage group.");

    // === AssignmentPeriod ===
    public static Error PeriodNotFound(Guid periodId) => Error.NotFound(
        "AssignmentPeriods.NotFound",
        $"The assignment period with Id = '{periodId}' was not found.");

    public static readonly Error InvalidPeriodRange = Error.Validation(
        "AssignmentPeriods.InvalidPeriodRange",
        "The assignment period end date must be greater than or equal to the start date.");

    public static readonly Error MissingService = Error.Validation(
        "AssignmentPeriods.MissingService",
        "Each assignment period must be associated with a valid hospital service.");
}
