BeforeAll {
    $assertion = Join-Path $PSScriptRoot 'Assert-AotCoverage.ps1'

    function New-CoverageReport {
        param(
            [int]$OnStartHits = 1,
            [int]$ConsoleHits = 0
        )

        $modules = @('GVFS', 'GVFS.Hooks', 'GVFS.Mount', 'GVFS.Service', 'GVFS.Common', 'GVFS.Platform.Windows', 'GVFS.Virtualization')
        $packages = ($modules | ForEach-Object {
            "<package name='$_'><classes><class name='$_.Type'><lines><line number='1' hits='1' /></lines></class></classes></package>"
        }) -join ''
        [xml]$document = "<coverage lines-covered='8' lines-valid='9'><packages>$packages</packages></coverage>"
        $service = $document.SelectSingleNode("//package[@name='GVFS.Service']/classes")
        $service.InnerXml = @"
<class name="GVFS.Service.GVFSService">
  <methods><method name="OnStart"><lines><line number="1" hits="$OnStartHits" /></lines></method></methods>
  <lines><line number="1" hits="$OnStartHits" /></lines>
</class>
<class name="GVFS.Service.Program">
  <methods><method name="RunAsConsoleApp"><lines><line number="42" hits="$ConsoleHits" /></lines></method></methods>
  <lines><line number="20" hits="1" /><line number="42" hits="$ConsoleHits" /></lines>
</class>
"@
        return ,$document
    }

    function Save-CoverageReport {
        param([xml]$Document)
        $path = Join-Path $TestDrive 'coverage.xml'
        $Document.Save($path)
        return $path
    }
}

Describe 'NativeAOT SCM coverage evidence' {
    It 'Accepts product coverage with SCM startup and no console-service entry' {
        $path = Save-CoverageReport (New-CoverageReport)
        $result = & $assertion -CoveragePath $path | ConvertFrom-Json
        $result.ScmServiceStartupCovered | Should -BeTrue
        $result.CompleteSuiteCoverage | Should -BeFalse
    }

    It 'Rejects a report without SCM startup execution' {
        $path = Save-CoverageReport (New-CoverageReport -OnStartHits 0)
        { & $assertion -CoveragePath $path } | Should -Throw '*no executed GVFSService.OnStart*'
    }

    It 'Rejects console-service execution even when SCM startup also executes' {
        $path = Save-CoverageReport (New-CoverageReport -ConsoleHits 1)
        { & $assertion -CoveragePath $path } | Should -Throw '*Console-service execution contaminates*'
    }

    It 'Rejects missing product modules' {
        $document = New-CoverageReport
        $node = $document.SelectSingleNode("//package[@name='GVFS.Mount']")
        [void]$node.ParentNode.RemoveChild($node)
        $path = Save-CoverageReport $document
        { & $assertion -CoveragePath $path } | Should -Throw '*Expected one coverage module for GVFS.Mount*'
    }

    It 'Rejects product modules with metadata but no executed lines' {
        $document = New-CoverageReport
        $document.SelectSingleNode("//package[@name='GVFS.Hooks']//line").SetAttribute('hits', '0')
        $path = Save-CoverageReport $document
        { & $assertion -CoveragePath $path } | Should -Throw '*No executed lines in required product module GVFS.Hooks*'
    }

    It 'Rejects unit-copy coverage in a product report' {
        $document = New-CoverageReport
        $node = $document.CreateElement('package')
        $node.SetAttribute('name', 'GVFS.UnitTests')
        [void]$document.coverage.packages.AppendChild($node)
        $path = Save-CoverageReport $document
        { & $assertion -CoveragePath $path } | Should -Throw '*Test assemblies contaminate*'
    }
}
