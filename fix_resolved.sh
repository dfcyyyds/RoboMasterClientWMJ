#!/bin/bash
echo "=== 修复 systemd-resolved DNS 问题 ==="

# 1. 停止 systemd-resolved
echo "停止 systemd-resolved..."
sudo systemctl stop systemd-resolved

# 2. 设置静态 DNS
echo "设置静态 DNS..."
echo "nameserver 114.114.114.114" | sudo tee /etc/resolv.conf
echo "nameserver 223.5.5.5" | sudo tee -a /etc/resolv.conf
echo "nameserver 8.8.8.8" | sudo tee -a /etc/resolv.conf
echo "options timeout:2 attempts:3" | sudo tee -a /etc/resolv.conf

# 3. 防止文件被覆盖
echo "保护 resolv.conf..."
sudo chattr -i /etc/resolv.conf 2>/dev/null || true
sudo chmod 644 /etc/resolv.conf

# 4. 重新激活网络
echo "重新激活网络..."
nmcli connection down "netplan-enx00e04c2f4988" 2>/dev/null
sleep 2
nmcli connection up "netplan-enx00e04c2f4988"

# 5. 测试
echo -e "\n=== 测试网络 ==="
echo "测试 DNS 解析:"
timeout 5 nslookup www.baidu.com || echo "nslookup 超时"

echo -e "\n测试 ping:"
ping -c 3 www.baidu.com

echo -e "\n测试 HTTP:"
curl -I https://baidu.com --connect-timeout 5 2>/dev/null | head -1 || echo "HTTP 测试失败"

echo -e "\n=== 当前配置 ==="
echo "resolv.conf 内容:"
cat /etc/resolv.conf

echo -e "\n网络连接状态:"
nmcli connection show "netplan-enx00e04c2f4988" | grep -E "ipv4\.dns|connected"

echo "=== 修复完成 ==="
