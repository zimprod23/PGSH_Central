using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Exports;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Export;

/// <summary>
/// The roll, as a spreadsheet: nom, prénom, Apogée, CNE, groupe — plus everything that names
/// <em>which</em> roll it is.
///
/// <para><b>It is an export of registrations, not of students.</b> Nom, prénom, CNE and Apogée belong
/// to a person and never move; niveau, groupe, partition and statut are facts about the year the
/// student is registered in, and 2 635 students in this base have sat in more than one. Cutting the
/// file from <c>Students</c> would have to pick one registration per row and could not say which —
/// so the row <em>is</em> the registration, and the year is part of its identity.</para>
///
/// <para>⚠ <b>An omitted year is the current one, never all of them</b> — the rule the whole
/// application is held to. Six promotions of history in a file labelled « liste des étudiants » is
/// the évaluation-import defect with a different button on it.</para>
///
/// <para><b>Why one sheet with a « Programme » and a « Niveau » column rather than a file per
/// promotion.</b> The columns cost nothing and make the file self-describing: merged with another
/// export, or opened a year later, a row still says which promotion it came from — which a file
/// whose only statement of scope was its name cannot do. The per-promotion file is not given up:
/// <c>levelId</c> produces exactly it, with the columns still in place.</para>
/// </summary>
public sealed record GetStudentsExportQuery(
    int? AcademicYearId = null,
    int? LevelId = null,
    AcademicProgram? Program = null,
    int? AcademicGroupId = null,
    /// <summary>
    /// The verdict recorded on the year's registration. Present so the file can take the <em>same</em>
    /// scope as the list it is downloaded from — a « liste des diplômés » on screen that exports the
    /// whole promotion is worse than no button.
    /// </summary>
    RegistrationStatus? Status = null,
    string? SearchTerm = null) : IQuery<ExportFile>;
