namespace Ailo.Jobs;

/// <summary>Optionally validates persisted JSON parameters before a job is saved.</summary>
public interface ICronJobParameterValidator
{
    void ValidateParametersJson(string parametersJson);
}
