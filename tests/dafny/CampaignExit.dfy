// RAIL-EXIT-001. The inputs are terminal outcomes after evidence publication.
module CampaignExit {
  datatype Status = Success | InvalidInput | MissingDependency | Failed | Cancelled | UnexpectedFailure | Unknown

  function ExitCode(status: Status): int
    ensures (ExitCode(status) == 0) <==> (status == Success)
  {
    match status
    case Success => 0
    case InvalidInput => 2
    case MissingDependency => 127
    case Cancelled => 130
    case _ => 1
  }
}
