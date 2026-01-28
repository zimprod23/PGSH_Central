using PGSH.Application.Abstractions.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Delete;

public record DeleteStudentCommand(Guid StudentId) : ICommand;