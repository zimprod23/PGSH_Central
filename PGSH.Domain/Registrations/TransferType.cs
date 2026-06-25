namespace PGSH.Domain.Registrations;

public enum TransferType
{
    // Scoped to a single stage: the student joins the target group only for that stage and
    // auto-reverts to the original group once the stage's periods complete.
    Temporary,

    // Permanent group change for the rest of the academic year: the registration's group moves
    // and every active assignment cascades to the target group's cohorts. No revert.
    Definitive
}
