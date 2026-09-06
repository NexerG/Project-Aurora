<#
    Stage 0 UI stress notes for the Thorium vault.

    All four rungs:    .\GenerateStressNotes.ps1
    One custom size:   .\GenerateStressNotes.ps1 -Chars 500000 -Name Stage0-0500k

    One GlyphControl is built per character, so the text-character count printed per rung is the
    control count the UI will carry. Output is deterministic for a given -Seed: the same seed twice
    writes byte-identical notes, and a larger rung is a prefix of every smaller one.
#>
param(
    [int]    $Chars = 0,
    [string] $Name  = 'Stage0-custom',
    [string] $Out   = (Join-Path $PSScriptRoot '..\Thorium\Data\Notes\Stress Test'),
    [int]    $Seed  = 20260905
)

$ErrorActionPreference = 'Stop'

$words = @(
    'a','advance','and','are','arrange','arranged','arranges','as','at','atlas','back','baked',
    'because','block','blocks','buffer','buffers','by','cache','caret','carries','column','columns',
    'command','control','controls','count','data','dense','descriptor','document','draw','drawn',
    'each','edge','element','engine','every','field','file','flow','font','frame','glyph','glyphs',
    'grows','handle','has','header','held','hit','holds','image','in','index','into','is','it',
    'layout','line','lines','list','live','main','measure','memory','mirror','not','of','on','one',
    'only','order','page','pane','panel','pass','path','per','point','pool','quad','range','rect',
    'renderer','rendered','row','rows','run','runs','scroll','set','shader','size','slot','space',
    'stack','state','style','styles','text','that','the','their','then','this','through','tick',
    'to','tree','under','until','update','upload','view','visible','walk','when','which','width',
    'window','with','writes','written'
)
$wordCount = $words.Length

# run styles the note format already proves, from SampleNote.xml
$runStyles = @(
    '',
    ' Bold="true"',
    ' Italic="true"',
    ' Bold="true" Italic="true"',
    ' ControlColor="gray"'
)
$gradientStyle = ' Gradient="accent" FontName="electrolize"'

$header = @'
<?xml version="1.0" encoding="utf-8"?>
<Document Name="{0}" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="http://arctisaurora/AuroraUITypes ../../XML/Schemas/UITypeSchema.xsd" xmlns="http://arctisaurora/AuroraUITypes">
'@

function New-Note
{
    param([int] $Target, [string] $Title, [int] $NoteSeed)

    $rng = [System.Random]::new($NoteSeed)
    $sb  = [System.Text.StringBuilder]::new($Target + [int]($Target / 3) + 4096)
    [void]$sb.AppendLine(($header -f $Title))

    $textChars = 0
    $blocks    = 0
    $runs      = 0

    while ($textChars -lt $Target)
    {
        $blocks++

        # a heading every ninth block, the top level every forty-fifth
        if ($blocks % 9 -eq 0)
        {
            $level = if ($blocks % 45 -eq 0) { 'Heading1' } else { 'Heading2' }
            $n = $rng.Next(3, 7)
            $head = [System.Text.StringBuilder]::new()
            for ($i = 0; $i -lt $n; $i++)
            {
                if ($i -gt 0) { [void]$head.Append(' ') }
                [void]$head.Append($words[$rng.Next(0, $wordCount)])
            }
            $title = $head.ToString()
            $title = $title.Substring(0, 1).ToUpper() + $title.Substring(1)

            [void]$sb.AppendLine(('  <Block StylingType="{0}">' -f $level))
            [void]$sb.AppendLine(('    <Run Text="{0}" />' -f $title))
            [void]$sb.AppendLine('  </Block>')
            $textChars += $title.Length
            $runs++
            continue
        }

        # one paragraph, built as whole sentences up to a random length
        $len  = $rng.Next(400, 901)
        $para = [System.Text.StringBuilder]::new()
        while ($para.Length -lt $len)
        {
            $n = $rng.Next(8, 19)
            $sentence = [System.Text.StringBuilder]::new()
            for ($i = 0; $i -lt $n; $i++)
            {
                if ($i -gt 0) { [void]$sentence.Append(' ') }
                [void]$sentence.Append($words[$rng.Next(0, $wordCount)])
            }
            $s = $sentence.ToString()
            [void]$para.Append($s.Substring(0, 1).ToUpper()).Append($s.Substring(1)).Append('. ')
        }
        $text = $para.ToString().TrimEnd()

        [void]$sb.AppendLine('  <Block>')

        # one in five paragraphs is cut into styled runs
        if ($rng.Next(0, 100) -lt 20)
        {
            $tokens = $text.Split(' ')
            $chunks = $rng.Next(3, 7)
            $per    = [Math]::Max(1, [int]($tokens.Length / $chunks))
            $idx    = 0

            for ($c = 0; $c -lt $chunks -and $idx -lt $tokens.Length; $c++)
            {
                $last = ($c -eq $chunks - 1)
                $take = if ($last) { $tokens.Length - $idx } else { [Math]::Min($per, $tokens.Length - $idx) }
                if ($take -le 0) { break }

                $chunk = [string]::Join(' ', $tokens[$idx..($idx + $take - 1)])
                if (-not $last) { $chunk += ' ' }
                $idx += $take

                $style = if ($c -eq 0) { '' }
                         elseif ($rng.Next(0, 100) -lt 2) { $gradientStyle }
                         else { $runStyles[$rng.Next(0, $runStyles.Length)] }

                [void]$sb.AppendLine(('    <Run{0} Text="{1}" />' -f $style, $chunk))
                $textChars += $chunk.Length
                $runs++
            }
        }
        else
        {
            [void]$sb.AppendLine(('    <Run Text="{0}" />' -f $text))
            $textChars += $text.Length
            $runs++
        }

        [void]$sb.AppendLine('  </Block>')
    }

    [void]$sb.AppendLine('</Document>')

    return [pscustomobject]@{
        Xml    = $sb.ToString()
        Chars  = $textChars
        Blocks = $blocks
        Runs   = $runs
    }
}

$rungs = @(
    @{ File = 'Stage0-0010k'; Chars = 10000    ; Title = 'Stage 0 - 10k chars'   },
    @{ File = 'Stage0-0050k'; Chars = 50000    ; Title = 'Stage 0 - 50k chars'   },
    @{ File = 'Stage0-0200k'; Chars = 200000   ; Title = 'Stage 0 - 200k chars'  },
    @{ File = 'Stage0-1000k'; Chars = 1000000  ; Title = 'Stage 0 - 1M chars'    }
)
if ($Chars -gt 0)
{
    $rungs = @(@{ File = $Name; Chars = $Chars; Title = $Name })
}

if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Path $Out -Force | Out-Null }
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

$report = foreach ($rung in $rungs)
{
    $note = New-Note -Target $rung.Chars -Title $rung.Title -NoteSeed $Seed
    $path = Join-Path $Out ($rung.File + '.xml')
    [System.IO.File]::WriteAllText($path, $note.Xml, $utf8Bom)

    [pscustomobject]@{
        Note     = $rung.File
        Glyphs   = $note.Chars
        Runs     = $note.Runs
        Blocks   = $note.Blocks
        Controls = $note.Chars + $note.Runs + $note.Blocks
        KB       = [int]((Get-Item $path).Length / 1KB)
    }
}

Write-Host ""
Write-Host ("Wrote to " + (Resolve-Path $Out).Path)
$report | Format-Table -AutoSize
