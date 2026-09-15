<?php
/**
 * Drop-in analytics endpoint for Assets/Analytics.
 *
 * Setup:
 * 1. Put this file in your game folder on the server, e.g. /Games/MyGame/analytics.php
 * 2. Ensure the web user can create/write the data/ directory next to this file
 * 3. In Unity Analytics prefab set baseUrl to https://your.host/.../MyGame/analytics.php
 *
 * Accepts POST JSON:
 * {
 *   "player_id": "...",
 *   "flag_name": "...",
 *   "playtime_session": 0,
 *   "playtime_player": 0,
 *   "platformName": "...",
 *   "device": "..."
 * }
 */

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(204);
    exit;
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    echo json_encode(['ok' => false, 'error' => 'POST only']);
    exit;
}

$raw = file_get_contents('php://input');
$data = json_decode($raw, true);

if (!is_array($data)) {
    http_response_code(400);
    echo json_encode(['ok' => false, 'error' => 'Invalid JSON']);
    exit;
}

$playerId = isset($data['player_id']) ? preg_replace('/[^a-zA-Z0-9_\-\.\@ ]/', '', (string)$data['player_id']) : '';
$flagName = isset($data['flag_name']) ? substr((string)$data['flag_name'], 0, 256) : '';
$playtimeSession = isset($data['playtime_session']) ? (int)$data['playtime_session'] : 0;
$playtimePlayer = isset($data['playtime_player']) ? (int)$data['playtime_player'] : 0;
$platformName = isset($data['platformName']) ? substr((string)$data['platformName'], 0, 64) : '';
$device = isset($data['device']) ? substr((string)$data['device'], 0, 64) : '';
$fps = isset($data['fps']) ? (float)$data['fps'] : 0;
if ($fps < 1 || $fps > 240) $fps = 0;

if ($playerId === '') {
    $playerId = 'unknown';
}

$dir = __DIR__ . DIRECTORY_SEPARATOR . 'data';
if (!is_dir($dir) && !mkdir($dir, 0755, true) && !is_dir($dir)) {
    http_response_code(500);
    echo json_encode(['ok' => false, 'error' => 'Cannot create data directory']);
    exit;
}

$csvPath = $dir . DIRECTORY_SEPARATOR . 'analytics.csv';
$isNew = !file_exists($csvPath);

$row = [
    date('c'),
    $playerId,
    $flagName,
    $playtimeSession,
    $playtimePlayer,
    $platformName,
    $device,
    $fps > 0 ? $fps : '',
    isset($_SERVER['REMOTE_ADDR']) ? $_SERVER['REMOTE_ADDR'] : '',
];

$fp = fopen($csvPath, 'ab');
if ($fp === false) {
    http_response_code(500);
    echo json_encode(['ok' => false, 'error' => 'Cannot open analytics.csv']);
    exit;
}

if (flock($fp, LOCK_EX)) {
    if ($isNew) {
        fputcsv($fp, ['timestamp', 'player_id', 'flag_name', 'playtime_session', 'playtime_player', 'platformName', 'device', 'fps', 'ip']);
    }
    fputcsv($fp, $row);
    flock($fp, LOCK_UN);
}
fclose($fp);

// Optional per-player latest snapshot (handy for dashboards)
$playerFile = $dir . DIRECTORY_SEPARATOR . 'players' . DIRECTORY_SEPARATOR . md5($playerId) . '.json';
$playersDir = dirname($playerFile);
if (!is_dir($playersDir)) {
    @mkdir($playersDir, 0755, true);
}
@file_put_contents($playerFile, json_encode([
    'player_id' => $playerId,
    'flag_name' => $flagName,
    'playtime_session' => $playtimeSession,
    'playtime_player' => $playtimePlayer,
    'platformName' => $platformName,
    'device' => $device,
    'fps' => $fps,
    'updated_at' => date('c'),
], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT));

echo json_encode(['ok' => true]);
