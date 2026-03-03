using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Services.GetById;

public record GetServiceByIdQuery(int Id) : IQuery<ServiceDetailResponse>;