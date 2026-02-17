#!/bin/bash

# トークン取得
echo "=== Getting token from Mock OIDC ==="
TOKEN=$(curl -s -X POST http://localhost:8081/default/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=test-client" \
  -d "client_secret=test-secret" \
  -d "audience=default" | jq -r '.access_token')

if [ -z "$TOKEN" ] || [ "$TOKEN" == "null" ]; then
  echo "Failed to get token"
  exit 1
fi
echo "Token acquired: ${TOKEN:0:20}..."

# デバイスリスト取得
echo -e "\n=== Getting device list ==="
DEVICES_RESPONSE=$(curl -s -X GET "http://localhost:8080/api/registry/devices?limit=5" \
  -H "Authorization: Bearer $TOKEN")
echo "$DEVICES_RESPONSE" | jq .

# デバイスIDを抽出して各デバイスの詳細を取得
# 修正: .items[].deviceId ではなく .items[].attributes.DeviceId を使用
echo -e "\n=== Getting details for each device ==="
DEVICE_IDS=$(echo "$DEVICES_RESPONSE" | jq -r '.items[].attributes.DeviceId // empty')

for DEVICE_ID in $DEVICE_IDS; do
  echo -e "\n--- Device: $DEVICE_ID ---"
  
  # 最新状態
  echo "Latest snapshot:"
  curl -s -X GET http://localhost:8080/api/devices/$DEVICE_ID \
    -H "Authorization: Bearer $TOKEN" | jq .
  
  # テレメトリ履歴
  echo "Recent telemetry:"
  curl -s -X GET "http://localhost:8080/api/telemetry/$DEVICE_ID?limit=5" \
    -H "Authorization: Bearer $TOKEN" | jq .
done
