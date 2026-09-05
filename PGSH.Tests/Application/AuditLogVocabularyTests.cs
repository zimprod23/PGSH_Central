using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Audit;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Le vocabulaire du journal, vérifié sur l'assemblage entier.
///
/// <para><b>Pourquoi ce fichier existe.</b> Les codes d'actes sont des chaînes littérales, déclarées
/// une par une dans une quarantaine de commandes. Rien ne les rassemble : une faute de frappe crée
/// un type d'acte de plus, une copie oubliée en fusionne deux, et le journal — dont tout l'intérêt
/// est qu'on puisse y chercher — devient inutilisable sans que rien n'échoue.</para>
///
/// <para>⚠ <b>Le contrôle appartient ici, pas à l'exécution.</b> Une garde qui lèverait au moment
/// d'enregistrer ferait tomber <i>l'acte</i> — une réinscription, une déliberation — pour un défaut
/// de programmation dans sa ligne de journal. Le coût serait payé par l'utilisateur au pire moment.
/// La réflexion sur l'assemblage attrape la même chose à la compilation des tests, où le coût est
/// nul. Même raisonnement que <c>AuditMetadataJson</c>, qui ne peut pas lever.</para>
/// </summary>
public class AuditLogVocabularyTests
{
    /// <summary>SCREAMING_SNAKE_CASE — la forme que portent les 45 actes existants.</summary>
    private static readonly Regex WellFormed = new("^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// Toute commande auditable, instanciée sans passer par ses constructeurs : les codes sont des
    /// propriétés calculées constantes, donc lisibles sur une instance non initialisée. C'est ce qui
    /// permet de les balayer sans connaître les paramètres de chacune.
    /// </summary>
    private static IEnumerable<(Type Command, IAuditableCommand Instance)> AuditableCommands()
    {
        var assembly = typeof(IAuditableCommand).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IAuditableCommand).IsAssignableFrom(type)) continue;

            var instance = (IAuditableCommand)System.Runtime.CompilerServices
                .RuntimeHelpers.GetUninitializedObject(type);

            yield return (type, instance);
        }
    }

    [Fact]
    public void Every_auditable_command_declares_a_well_formed_action_and_entity_type()
    {
        var commands = AuditableCommands().ToList();

        commands.Should().NotBeEmpty("the sweep is worthless if it finds nothing to sweep");

        foreach (var (type, command) in commands)
        {
            command.AuditAction.Should().NotBeNullOrWhiteSpace(
                $"{type.Name} records an act, so it has to say which");

            command.AuditAction.Should().MatchRegex(WellFormed,
                $"{type.Name}'s action has to read like the other 45 — a journal is searched by these");

            command.AuditEntityType.Should().NotBeNullOrWhiteSpace(
                $"{type.Name} has to say what its act was about");
        }
    }

    /// <summary>
    /// ⚠ <b>Deux commandes différentes partageant un code fusionnent deux actes dans le journal</b>,
    /// et rien ne le signale : la puce de filtrage en compte simplement plus, et « qui a fait ça »
    /// renvoie des lignes qui parlent d'autre chose. C'est le défaut qu'une copie de fichier produit
    /// naturellement, et il est invisible à la relecture parce que les deux déclarations sont dans
    /// deux fichiers.
    /// </summary>
    [Fact]
    public void No_two_commands_share_an_action_code()
    {
        var byAction = AuditableCommands()
            .GroupBy(c => c.Instance.AuditAction)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ← {string.Join(", ", g.Select(c => c.Command.Name))}")
            .ToList();

        byAction.Should().BeEmpty(
            "each act needs its own name, or the journal cannot tell two of them apart");
    }

    /// <summary>
    /// La garde du domaine, en dessous du balayage : elle n'attrape rien que le test ci-dessus laisse
    /// passer, et elle existe pour que l'entité ne dépende pas de l'existence de ce fichier.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_entry_without_an_action_is_refused(string action)
    {
        var build = () => AuditLog.Record(
            action, "AcademicYear", "1", null, null, new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc));

        build.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// L'instant vient du paramètre, jamais de l'horloge de la machine — c'est ce qui rend la date
    /// d'une entrée vérifiable, et c'est la règle que suit tout le reste du domaine.
    /// </summary>
    [Fact]
    public void The_moment_of_an_entry_is_the_one_it_is_given()
    {
        var moment = new DateTime(2026, 9, 4, 11, 30, 0, DateTimeKind.Utc);

        AuditLog.Record("GROUP_CREATED", "AcademicYear", "22", null, null, moment)
            .CreatedAt.Should().Be(moment);
    }
}
