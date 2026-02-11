#!/bin/bash
echo "=== 最终网络修复 ==="

# 1. 停止并禁用 systemd-resolved
echo "禁用 systemd-resolved..."
sudo systemctl stop systemd-resolved 2>/dev/null
sudo systemctl disable systemd-resolved 2>/dev/null
sudo systemctl mask systemd-resolved 2>/dev/null

# 2. 删除旧的 resolv.conf
echo "修复 resolv.conf..."
sudo rm -f /etc/resolv.conf
sudo rm -f /run/systemd/resolve/stub-resolv.conf

# 3. 创建新的静态 DNS 配置
echo "nameserver 114.114.114.114" | sudo tee /etc/resolv.conf
echo "nameserver 223.5.5.5" | sudo tee -a /etc/resolv.conf
echo "nameserver 8.8.8.8" | sudo tee -a /etc/resolv.conf
echo "options timeout:2 attempts:3" | sudo tee -a /etc/resolv.conf

# 4. 锁定文件
sudo chattr +i /etc/resolv.conf 2>/dev/null || echo "无法锁定文件，继续..."

# 5. 禁用 IPv6（可选，如果 IPv4 有问题）
echo "配置网络连接..."
nmcli connection modify "netplan-enx00e04c2f4988" \
  ipv6.method disabled \
  ipv4.dns "114.114.114.114 223.5.5.5" \
  ipv4.ignore-auto-dns yes

# 6. 重新激活网络
nmcli connection down "netplan-enx00e04c2f4988" 2>/dev/null
sleep 2
nmcli connection up "netplan-enx00e04c2f4988"

# 7. 测试
echo -e "\n=== 网络测试 ==="

echo "1. 测试 IPv4 DNS:"
nslookup www.baidu.com

echo -e "\n2. 测试 IPv4 ping:"
ping -4 -c 3 www.baidu.com

echo -e "\n3. 测试网站访问:"
echo "百度:"
curl -I https://www.baidu.com --connect-timeout 5 2>/dev/null | head -1
echo -e "\nGitHub:"
curl -I https://github.com --connect-timeout 5 2>/dev/null | head -1

echo -e "\n4. 查看当前配置:"
echo "resolv.conf:"
cat /etc/resolv.conf
echo -e "\n网络接口:"
ip -4 addr show enx00e04c2f4988

echo "=== 修复完成 ==="
