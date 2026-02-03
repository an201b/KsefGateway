try {
    Invoke-RestMethod -Method Post -Uri "http://localhost:5062/api/debug/init-session"
} catch {
    Write-Host "❌ ОШИБКА ($($_.Exception.Response.StatusCode))" -ForegroundColor Red
    if ($_.Exception.Response) {
        $stream = $_.Exception.Response.GetResponseStream()
        $reader = [System.IO.StreamReader]::New($stream)
        $body = $reader.ReadToEnd()
        Write-Host "📜 ОТВЕТ KSeF:" -ForegroundColor Yellow
        Write-Host $body -ForegroundColor White
    }
}