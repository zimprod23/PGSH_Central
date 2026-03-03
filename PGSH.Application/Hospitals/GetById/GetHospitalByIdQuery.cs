using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.GetById;

public record GetHospitalByIdQuery(int Id) : IQuery<HospitalDetailResponse>;