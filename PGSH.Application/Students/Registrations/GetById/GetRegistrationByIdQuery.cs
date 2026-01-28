using PGSH.Application.Abstractions.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Registrations.GetById;

public sealed record GetRegistrationByIdQuery(Guid Id) : IQuery<RegistrationResponse>;
