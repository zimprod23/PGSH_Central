using MediatR;
using PGSH.SharedKernel;

namespace PGSH.Application.Abstractions.Messaging;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
