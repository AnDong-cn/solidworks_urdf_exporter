param (
    [string]$filename
 )

$CommitVersion = git describe --tags --long --dirty --always
$CommitVersion += "-step.1"
$FileContent = 'using System.Reflection;

[assembly: AssemblyInformationalVersion("{0}")]' -f $CommitVersion
$FileContent | Out-File $filename
