using PGSH.Domain.Common.Utils;
﻿using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Students.GetById;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.GetMany;

/// <param name="PromotionsOnly">
/// Keep only levels that are a year of study (<see cref="Level.IsPromotion"/>). ⚠ Off by default and
/// deliberately so: « Retrait » is year 0, and the student dossier, the parcours and the registration
/// history all have to be able to name the level a withdrawn registration carries. It is the screens
/// that ask « which promotion am I planning? » that must pass <c>true</c> — a picker offering
/// « Retrait » beside « Troisième Année » invites an act that is then refused, and once was not.
/// </param>
public sealed record GetLevelsQuery(
    string? SearchTerm,
    AcademicProgram? AcademicProgram,
    int PageNumber = 1,
    int PageSize = 20,
    bool PromotionsOnly = false): IQuery<PaginatedResponse<LevelResponse>>;
