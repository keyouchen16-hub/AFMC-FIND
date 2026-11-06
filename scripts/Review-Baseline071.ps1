$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'docs/evidence/v071'
$review = Get-Content (Join-Path $folder 'baseline-review.json') -Raw | ConvertFrom-Json
# Codex's explicit title/metadata review, not content verification or a blinded holdout.
$before = @{1='UURUU';5='NNUNN';11='RRNRN';16='NNNNN';23='RNRRR';28='NNNRN';29='RRNRR';31='RNRRR';32='NNNNN';34='UURRU';37='RRNNN';38='UUUUU';41='RNRRN';42='NRNRR';44='URRUU';45='UUUUU';46='NNNRR';47='UUUUU';48='URNRN';49='UNNRN';50='NNNNR'}
$after = @{32='NRNNN';34='UURRU';41='RRRRN';44='URUUR';46='RNNRN';47='RRRRU';48='RRUUR';50='NNNNN'}
$notes = @{5='Intent: first film including extended edition; sequel alone is N.';12='Season or individual episodes within requested season count as relevant; not a completeness claim.';17='Broad Three Body adaptations, no country/year specified.';19='Broad Naruto franchise including Shippuden.';21='Broad One Piece franchise.';27='OS images; no latest-release requirement.';28='OS images and named flavours, not applications running on this OS.';29='OS images and named flavours.';31='OS images, not books or comparison videos.';32='Blender application, not plugins/courses; 4.2.1 satisfies 4.2 family.';34='Suspicious version/crack metadata remains U; no executable was opened.';37='Bach music including arrangements, not namesakes/books.';41='Jane Austen original, not derivative fiction.';42='Lewis Carroll original, not derivative fiction.';44='ML learning material; bare names/archives remain U.';45='Broad work/adaptations; no medium specified.';46='Conan Doyle stories; derivative fiction/nonfiction is N.';47='MNIST datasets; variants with unclear identity remain U.';48='CIFAR datasets; unexplained bare names remain U.';49='ImageNet datasets/subsets, not research papers.';50='Chosen audit intent: encyclopedia dump. Query itself is ambiguous; regression retained, requires category intent.'}
$rows = @()
foreach ($q in $review.Queries) {
    $id = [int]$q.Id
    $labels = @{Before=$(if($before.ContainsKey($id)){$before[$id]}else{'RRRRR'}); After=$(if($after.ContainsKey($id)){$after[$id]}else{'RRRRR'})}
    $entry = [ordered]@{Id=$id;Query=$q.Query;IntentNote=$(if($notes.ContainsKey($id)){$notes[$id]}else{'Title and explicit query constraints; insufficient bare metadata is U. No content validation.'})}
    foreach($side in 'Before','After') {
        if($q.$side.Count -ne 5){throw "Unexpected candidate count: $id $side"}
        $entry[$side]=@(for($i=0;$i -lt 5;$i++) { [ordered]@{Rank=$i+1;Label=[string]$labels[$side][$i];Title=$q.$side[$i].Title;Hash=$q.$side[$i].Hash;Source=$q.$side[$i].Source} })
    }
    $rows += [pscustomobject]$entry
}
$metrics = [ordered]@{}
foreach($side in 'Before','After') {
    $all = @($rows | ForEach-Object {$_.${side}})
    $metrics[$side]=[ordered]@{Top1Relevant=@($rows | Where-Object {$_.${side}[0].Label -eq 'R'}).Count;HitAt5=@($rows | Where-Object {$_.${side}.Label -contains 'R'}).Count;Relevant=@($all | Where-Object Label -eq R).Count;Uncertain=@($all | Where-Object Label -eq U).Count;NonRelevant=@($all | Where-Object Label -eq N).Count}
}
$times=@($review.Queries.ElapsedMs | Sort-Object);$first=@($review.Queries.FirstResultMs | Sort-Object)
$failures=@(Get-ChildItem (Join-Path $folder 'baseline') -Filter '*.json' | ForEach-Object { $s=Get-Content $_.FullName -Raw | ConvertFrom-Json; foreach($source in $s.Sources){if($source.Error){[ordered]@{Query=$s.Query;Source=$source.Name;Code=$source.FailureCode;Message=$source.Error}}} })
$result=[ordered]@{Reviewer='Codex title/metadata review';Labels=@{R='Matches declared audit intent by metadata only';N='Does not match declared audit intent';U='Insufficient or suspicious metadata'};Limit='Exploratory development set used to tune ranking; not blind or independent; no resource bodies downloaded. Before is reconstructed on the same captured records, not a second live run. Intent is reviewer-declared, not user labels.';Queries=50;Candidates=500;Metrics=$metrics;Network=@{SourceReports=550;Failures=$failures;MedianCompletionMs=($times[24]+$times[25])/2;P95CompletionMs=$times[47];MedianFirstResultMs=($first[24]+$first[25])/2;MaxPagesPerSource=1};Judgments=$rows}
$result | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $folder 'baseline-judgments.json') -Encoding utf8
$metrics | ConvertTo-Json -Depth 5
$result.Network | ConvertTo-Json -Depth 5
