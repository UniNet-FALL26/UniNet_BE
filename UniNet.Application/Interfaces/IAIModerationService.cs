using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Application.DTOs.Projects;

namespace UniNet.Application.Interfaces
{
    public interface IAIModerationService
    {
        Task<AiModerationResult> AnalyzeProjectAsync(
        ProjectModerationInput input,
        CancellationToken cancellationToken = default);
    }
}
