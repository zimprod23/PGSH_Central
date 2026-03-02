using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Centers.GetById;

public sealed record GetCenterByIdQuery(int HospitalId) : IQuery<CenterDetailResponse>;
