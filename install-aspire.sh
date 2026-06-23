#!/usr/bin/env bash

RED='\033[1;31m'
GRN='\033[1;32m'
YLW='\033[1;33m'
ORG='\033[38;5;208m'
CYN='\033[1;36m'
BLU='\033[1;34m'
RST='\033[0m'

banner() {
    echo -e "${BLU}"
    echo "   _____       __"
    echo "  / ___/____  / /___ _________"
    echo "  \__ \/ __ \/ / __ \`/ ___/ _ \\"
    echo " ___/ / /_/ / / /_/ / /__/  __/"
    echo "/____/\____/_/\__,_/\___/\___/"
    echo -e "${RST}"
}

help_text() {
    echo ""
    echo -e "${CYN}Usage:${RST} install.sh [OPTIONS]"
    echo ""
    echo "Install Solace - a Minecraft Earth replacement server."
    echo ""
    echo -e "${CYN}Options:${RST}"
    echo "  -h, --help     Show this help message"
    echo ""
    echo -e "${CYN}Platforms:${RST}"
    echo "  Termux (Android)   Auto-detected, uses proot-distro"
    echo "  Linux              Auto-detected, uses systemd"
    echo "  macOS              Auto-detected, uses launchd"
    echo ""
    echo "After installation, run: earth"
    exit 0
}

for arg in "$@"; do
    case "$arg" in
        -h|--help) help_text ;;
    esac
done

print_step() {
    echo ""
    echo -e "${CYN}========================================${RST}"
    echo -e "${CYN}  $1${RST}"
    echo -e "${CYN}========================================${RST}"
}

print_sub() {
    echo -e "  ${BLU}>${RST} $1"
}

ok()   { echo -e "${GRN}[OK] $1${RST}"; }
skip() { echo -e "${YLW}[SKIP] $1${RST}"; }
err()  { echo -e "${RED}[ERROR] $1${RST}"; exit 1; }

GITHUB_REPO="BitcoderCZ/Solace"
GITHUB_URL="https://github.com/$GITHUB_REPO.git"

banner

# ─────────────────────────────────────────
#  TERMUX
# ─────────────────────────────────────────
if [ -n "$TERMUX_VERSION" ] || echo "$PREFIX" | grep -q "com.termux"; then
    export DEBIAN_FRONTEND=noninteractive
    dpkg --configure -a >/dev/null 2>&1 || true

    print_step "1. CHECKING PROOT-DISTRO"
    if ! command -v proot-distro >/dev/null 2>&1; then
        pkg update -y
        pkg install -y -o Dpkg::Options::="--force-confnew" proot-distro || {
            dpkg --configure -a
            pkg install -y -o Dpkg::Options::="--force-confnew" proot-distro
        }
        hash -r
        command -v proot-distro >/dev/null || err "proot-distro install failed"
        ok "Installed proot-distro"
    else
        skip "Already installed"
    fi

    print_step "2. CHECKING UBUNTU"
    if proot-distro login ubuntu -- true 2>/dev/null; then
        skip "Ubuntu already installed"
    else
        proot-distro install ubuntu
        ok "Ubuntu installed"
    fi

    clear && banner
    print_step "SELECT BRANCH"
    echo ""
    echo -e "${CYN}Select branch:${RST}"
    echo ""
    echo -e "  ${GRN}[1] Main (stable - recommended)${RST}"
    echo -e "  ${YLW}[2] Dev (unstable - may break)${RST}"
    echo ""
    printf "Choice [1/2] > "
    read -r BRANCH_CHOICE < /dev/tty
    BRANCH_CHOICE="$(echo "$BRANCH_CHOICE" | tr -d '\r\n')"

    ARTIFACT_PREFIX="Solace"
    INSTALL_BRANCH="main"
    SELECTED_TAG=""
    case "$BRANCH_CHOICE" in
        2|dev|Dev)
            ARTIFACT_PREFIX="Solace-Dev"
            INSTALL_BRANCH="dev"
            SELECTED_TAG="dev-build"
            echo -e "${YLW}[INFO] Using Dev build${RST}"
            ;;
        *)
            echo "[INFO] Fetching releases..."
            RELEASE_JSON=$(curl -s "https://api.github.com/repos/$GITHUB_REPO/releases?per_page=100")
            SELECTED_TAG=$(echo "$RELEASE_JSON" | grep -o '"tag_name": *"[^"]*"' | sed 's/"tag_name": *"//;s/"//' | grep -v "^dev-build$" | head -n1)
            [ -z "$SELECTED_TAG" ] && err "No releases found."
            echo "[INFO] Latest main release: $SELECTED_TAG"
            ;;
    esac

    ZIP_NAME="${ARTIFACT_PREFIX}-linux-arm64.zip"
    URL="https://github.com/$GITHUB_REPO/releases/download/${SELECTED_TAG}/${ZIP_NAME}"

    print_step "3. CONFIGURING UBUNTU"
    proot-distro login ubuntu -- bash << EOF 2>/dev/null
echo "[1] System update"
apt update -y

echo "[2] Installing dependencies"
apt install -y wget fzf curl unzip gnupg software-properties-common \
    apt-transport-https ca-certificates openjdk-21-jre libicu-dev

if ! command -v pwsh >/dev/null 2>&1; then
    echo "[3] Installing PowerShell"
    mkdir -p /opt/microsoft/powershell/7
    cd /opt/microsoft/powershell/7
    wget -q https://github.com/PowerShell/PowerShell/releases/download/v7.6.1/powershell-7.6.1-linux-arm64.tar.gz
    tar zxf powershell-7.6.1-linux-arm64.tar.gz
    chmod +x pwsh
    ln -sf /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh
fi

if [ ! -d "$HOME/.dotnet" ] || ! "$HOME/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q "^10\."; then
    echo "[4] Installing .NET 10"
    cd ~
    wget -q https://dot.net/v1/dotnet-install.sh
    chmod +x dotnet-install.sh
    ./dotnet-install.sh --channel 10.0
fi

grep -q DOTNET_ROOT ~/.bashrc || echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
grep -q ".dotnet/tools" ~/.bashrc || echo 'export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools' >> ~/.bashrc
grep -q COMPlus_gcServer ~/.bashrc || {
    echo 'export COMPlus_gcServer=0'         >> ~/.bashrc
    echo 'export COMPlus_gcConcurrent=1'     >> ~/.bashrc
    echo 'export DOTNET_GCHeapHardLimit=268435456' >> ~/.bashrc
}

echo "[5] Installing .NET Aspire..."
curl -sSL https://aspire.dev/install.sh | bash

mkdir -p ~/Solace

echo "[6] Downloading pre-compiled server"
cd ~

if [ -z "$SELECTED_TAG" ]; then
    echo "[ERROR] No release tag found"
    exit 1
fi

echo "[INFO] Downloading ${SELECTED_TAG}..."
curl -L --progress-bar -o "$ZIP_NAME" "$URL" || { echo "[ERROR] Download failed"; exit 1; }
echo -e "  ${GRN}✔${RST} Download complete"
echo -ne "  ${BLU}>${RST} Extracting... "
unzip -o "$ZIP_NAME" >/dev/null 2>&1 && echo -e "${GRN}done${RST}" || { echo -e "${RED}failed${RST}"; exit 1; }
rm -rf ~/Solace/*
echo "$SELECTED_TAG" > ~/Solace/version.txt

if [ -d Solace-linux-arm64 ]; then
    mv Solace-linux-arm64/* ~/Solace/
    rm -rf Solace-linux-arm64
else
    mv components       ~/Solace/ 2>/dev/null || true
    mv launcher         ~/Solace/ 2>/dev/null || true
    mv staticdata       ~/Solace/ 2>/dev/null || true
fi

chmod -R +x ~/Solace/components/ 2>/dev/null || true

cat > ~/Solace/settings.json << JSONEOF
{
  "installMode": "prebuilt",
  "branch": "$INSTALL_BRANCH",
  "version": "$SELECTED_TAG",
  "updatedAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
JSONEOF

echo "[6] Cleaning installer leftovers"
rm -f ~/dotnet-install.sh
rm -f ~/Solace-linux-arm64.zip

echo "[DONE]"
EOF

    ok "Ubuntu configured"

    print_step "4. CREATING EARTH COMMAND"
    mkdir -p "$PREFIX/bin"
    
    EARTH_TARGET_DIR="~/Solace/launcher"
    STATICDATA_PATH="~/Solace/staticdata"

cat << EOF > $PREFIX/bin/earth
#!/bin/bash
(
    cd "$EARTH_TARGET_DIR" || exit 13

    STATICDATA_PATH="$STATICDATA_PATH"

    SERVER_JAR_NAME="fabric-server-mc.1.20.4-loader.0.15.10-launcher.1.0.1.jar"
    RESOURCENAME="vanilla.zip"
    RESOURCE_DIR="\$STATICDATA_PATH/resourcepacks"
    TEMPLATE_DIR="\$STATICDATA_PATH/server_template_dir"
    MODS_DIR="\$TEMPLATE_DIR/mods"
    EULA_PATH="\$TEMPLATE_DIR/eula.txt"
    RESOURCEPACK_PATH="\$RESOURCE_DIR/\$RESOURCENAME"

    proot-distro login ubuntu -- env SERVER_JAR_NAME="\$SERVER_JAR_NAME" RESOURCENAME="\$RESOURCENAME" RESOURCE_DIR="\$RESOURCE_DIR" TEMPLATE_DIR="\$TEMPLATE_DIR" MODS_DIR="\$MODS_DIR" EULA_PATH="\$EULA_PATH" RESOURCEPACK_PATH="\$RESOURCEPACK_PATH" bash << 'DASHBOARD'
#!/bin/bash
(
    # 1. Resource Pack Check
    if [ ! -f "\\\$RESOURCEPACK_PATH" ]; then
        echo "ERROR: Resourcepack file '\\\$RESOURCEPACK_PATH' is missing."
        echo "Download it from: https://cdn.mceserv.net/availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35" - using e.g. https://archive.org
        echo "Rename it to \\\$RESOURCENAME and move it to: \\\$RESOURCE_DIR"
        exit 1
    fi

    FILE_SIZE=\\\$(stat -c%s "\$RESOURCEPACK_PATH" 2>/dev/null || echo 0)

    if [ "\\\$FILE_SIZE" -lt 100000000 ]; then
        echo "ERROR: Resourcepack file '\\\$RESOURCEPACK_PATH' is too small or invalid."
        echo "Download it from: https://cdn.mceserv.net/availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35" - using e.g. https://archive.org
        echo "Rename it to \\\$RESOURCENAME and move it to: \\\$RESOURCE_DIR"
        exit 1
    fi

    # 2. Fabric API Check
    if ! ls "\\\$MODS_DIR"/fabric-api-*.jar 1> /dev/null 2>&1; then
        echo "Fabric API not found, downloading..."
        mkdir -p "\\\$MODS_DIR"
        curl -o "\\\$MODS_DIR/fabric-api-0.97.0+1.20.4.jar" -L "https://cdn.modrinth.com/data/P7dR8mSH/versions/xklQBMta/fabric-api-0.97.0%2B1.20.4.jar"
        echo "Downloaded fabric api."
    fi

    # 3. Fabric Server Jar Check
    if [ ! -f "\\\$TEMPLATE_DIR/\\\$SERVER_JAR_NAME" ]; then
        echo "Fabric server not found, downloading..."
        mkdir -p "\\\$TEMPLATE_DIR"
        curl -o "\\\$TEMPLATE_DIR/\\\$SERVER_JAR_NAME" -L "https://meta.fabricmc.net/v2/versions/loader/1.20.4/0.15.10/1.0.1/server/jar"
        echo "Downloaded fabric server."
    fi

    run_server() {
        echo "Running server..."
        cd "\\\$TEMPLATE_DIR" || exit
        java -jar "\\\$SERVER_JAR_NAME" -nogui
        local exit_code=$?
        echo "Server process exited with code \\\$exit_code"
        return \\\$exit_code
    }

    # 4. EULA Setup
    if [ ! -f "\\\$EULA_PATH" ]; then
        run_server
        if [ $? -ne 0 ]; then exit 1; fi
    fi

    # 5. EULA Verification Loop
    if grep -iq "eula=false" "\\\$EULA_PATH" || ! grep -iq "eula=true" "\\\$EULA_PATH"; then
        echo "===================================================="
        echo " Minecraft End User License Agreement (EULA)"
        echo "===================================================="
        echo ""
        
        if [ -f "\\\$EULA_PATH" ]; then
            grep "^#" "\\\$EULA_PATH"
        else
            echo "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA)."
        fi
        
        echo "===================================================="
        
        while true; do
            read -p "Type 'accept' to accept the EULA and continue: " user_input
            if [ "\\\${user_input,,}" = "accept" ]; then
                if grep -q "eula=" "\\\$EULA_PATH"; then
                    sed -i 's/eula=.*/eula=true/I' "\\\$EULA_PATH"
                else
                    echo "eula=true" >> "\\\$EULA_PATH"
                fi
                echo "EULA accepted successfully."
                break
            else
                echo "Invalid input. You must type 'accept' to proceed."
            fi
        done
        
        echo "Running server to generate remaining files..."
        run_server
        if [ $? -ne 0 ]; then exit 1; fi
    fi

    cd "$EARTH_TARGET_DIR" || exit 13

    ./Launcher
)
DASHBOARD
)
EOF
    
    chmod +x "$PREFIX/bin/earth"
    ok "earth command installed"

    echo ""
    echo -e "${GRN}========================================${RST}"
    echo -e "${ORG}           INSTALL COMPLETE             ${RST}"
    echo -e "${GRN}========================================${RST}"
    echo ""
    echo -e "  ${CYN}User:${RST}    $(whoami)"
    echo -e "  ${CYN}OS:${RST}      Termux (proot-distro ubuntu)"
    echo -e "  ${CYN}Arch:${RST}    $(uname -m)"
    echo -e "  ${CYN}Mode:${RST}    prebuilt"
    echo -e "  ${CYN}Branch:${RST}  $INSTALL_BRANCH"
    echo -e "  ${CYN}Server:${RST}  ~/Solace"
    echo ""
    echo -e "${CYN}Next steps:${RST}"
    echo "  1. Download the resource packs (refer to Discord for the commands)"
    echo "  2. Run: earth"
    echo "  3. Open http://127.0.0.1:5000 and create your admin account"
    echo "  4. Under 'Server Options', set Network/IPv4 Address to your PC's IP"
    echo "  5. Get a MapTiler API key: https://cloud.maptiler.com/account/keys/"
    echo "  6. Under 'Server Status', click Start"
    echo "  7. Accept the Minecraft EULA when prompted in the logs"
    echo ""
    echo -e "${CYN}Useful commands:${RST}"
    echo "  earth              TUI menu"
    echo "  earth uninstall    remove Solace completely"
    echo ""
    exit 0
fi

if [ -n "$SUDO_USER" ]; then
    CURRENT_USER="$SUDO_USER"
else
    CURRENT_USER=$(whoami)
fi

HOME_DIR=$(eval echo "~$CURRENT_USER")
SOLACE_DIR="$HOME_DIR/solace"
SERVER_DIR="$SOLACE_DIR/server"
SOURCE_DIR="$SOLACE_DIR/source"
SERVICE_FILE="/etc/systemd/system/solace.service"
SETTINGS_FILE="$SOLACE_DIR/settings.json"
VERSION_FILE="$SOLACE_DIR/version.txt"

OS=$(uname -s)
case $(uname -m) in
    x86_64)        ARCH_PROFILE="x64"   ; JAVA_ARCH="amd64" ;;
    aarch64|arm64) ARCH_PROFILE="arm64" ; JAVA_ARCH="arm64" ;;
    *) err "Unsupported architecture: $(uname -m)" ;;
esac

if [ "$OS" = "Darwin" ]; then
    PROFILE="osx-$ARCH_PROFILE"
else
    PROFILE="linux-$ARCH_PROFILE"
fi

export DOTNET_ROOT="$HOME_DIR/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

ASPIRE_BIN="$HOME_DIR/.aspire/bin/aspire"

detect_pkg_manager() {
    if [ "$OS" = "Darwin" ]; then
        PKG_MANAGER="brew"
    elif command -v apt-get &>/dev/null; then
        PKG_MANAGER="apt"
    elif command -v dnf &>/dev/null; then
        PKG_MANAGER="dnf"
    elif command -v pacman &>/dev/null; then
        PKG_MANAGER="pacman"
    elif command -v zypper &>/dev/null; then
        PKG_MANAGER="zypper"
    else
        err "No supported package manager found (apt, dnf, pacman, zypper, brew)."
    fi
    ok "Detected package manager: $PKG_MANAGER"
}

pkg_install() {
    case $PKG_MANAGER in
        apt)    apt-get install -y "$@" ;;
        dnf)    dnf install -y "$@" ;;
        pacman) pacman -S --noconfirm "$@" ;;
        zypper) zypper install -y "$@" ;;
        brew)   sudo -u "$CURRENT_USER" brew install "$@" ;;
    esac
}

pkg_update() {
    case $PKG_MANAGER in
        apt)    apt-get update -qq ;;
        dnf)    dnf check-update -q || true ;;
        pacman) pacman -Sy --noconfirm ;;
        zypper) zypper refresh ;;
        brew)   sudo -u "$CURRENT_USER" brew update ;;
    esac
}

install_java() {
    print_sub "Installing Java 17..."
    case $PKG_MANAGER in
        apt)    pkg_install openjdk-17-jre ;;
        dnf)    pkg_install java-17-openjdk ;;
        pacman) pkg_install jre17-openjdk ;;
        zypper) pkg_install java-17-openjdk ;;
        brew)   pkg_install openjdk@17 ;;
    esac
}

install_pwsh() {
    print_sub "Installing PowerShell..."
    case $PKG_MANAGER in
        apt)
            wget -q "https://packages.microsoft.com/config/$(. /etc/os-release && echo "$ID")/$(. /etc/os-release && echo "$VERSION_ID")/packages-microsoft-prod.deb" \
                -O /tmp/packages-microsoft-prod.deb 2>/dev/null \
            || wget -q "https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb" \
                -O /tmp/packages-microsoft-prod.deb
            dpkg -i /tmp/packages-microsoft-prod.deb 2>/dev/null || true
            apt-get update -qq
            pkg_install powershell
            ;;
        dnf)
            rpm --import https://packages.microsoft.com/keys/microsoft.asc
            dnf install -y "https://packages.microsoft.com/rhel/9/prod/packages-microsoft-prod.rpm" 2>/dev/null || true
            pkg_install powershell
            ;;
        pacman)
            sudo -u "$CURRENT_USER" bash -c "
                git clone https://aur.archlinux.org/powershell-bin.git /tmp/powershell-bin 2>/dev/null || true
                cd /tmp/powershell-bin && makepkg -si --noconfirm 2>/dev/null || true
            " 2>/dev/null || pkg_install powershell-bin 2>/dev/null || pkg_install powershell 2>/dev/null || true
            ;;
        zypper)
            rpm --import https://packages.microsoft.com/keys/microsoft.asc
            zypper addrepo https://packages.microsoft.com/rhel/9/prod/ microsoft 2>/dev/null || true
            pkg_install powershell
            ;;
        brew)
            pkg_install powershell
            ;;
    esac
}

# ─── STEP 1: ROOT CHECK ────────────────────────────────────

print_step "PRE-FLIGHT CHECK"
if [ "$OS" != "Darwin" ] && [ "$EUID" -ne 0 ]; then
    err "Please run the script as root (sudo)!"
fi
detect_pkg_manager

# ─── STEP 2: DEPENDENCY CHECK ──────────────────────────────

MISSING_DEPS=()

check_dep() {
    if ! command -v "$1" >/dev/null 2>&1 && [ ! -f "$HOME_DIR/.aspire/bin/$1" ]; then
        MISSING_DEPS+=("$1 ($2)")
    else
        skip "$1 already installed"
    fi
}

check_dep_aspire() {
    if command -v aspire >/dev/null 2>&1; then
        skip "aspire already installed"
    elif [ -f "$ASPIRE_BIN" ]; then
        skip "aspire already installed at $ASPIRE_BIN"
    else
        MISSING_DEPS+=("aspire (.NET Aspire CLI)")
    fi
}

check_dep "java"   "Java 17+ JRE"
check_dep "pwsh"   "PowerShell 7+"
check_dep "curl"   "curl"
check_dep "unzip"  "unzip"
check_dep "git"    "git"
check_dep "fzf"    "fzf"
check_dep_aspire

DOTNET_MISSING=false
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q "^10\."; then
    DOTNET_MISSING=true
    MISSING_DEPS+=("dotnet (.NET 10 SDK)")
else
    skip ".NET 10 already installed"
fi

if [ ${#MISSING_DEPS[@]} -gt 0 ]; then
    echo ""
    echo -e "${YLW}Missing dependencies:${RST}"
    for dep in "${MISSING_DEPS[@]}"; do
        echo -e "  ${RED}✗${RST} $dep"
    done
    echo ""
    echo -e "${CYN}Install missing dependencies now?${RST}"
    echo ""
    printf "Install now? [Y/n] > "
    read -r INSTALL_DEPS < /dev/tty
    INSTALL_DEPS="$(echo "$INSTALL_DEPS" | tr -d '\r\n')"

    if [ "$INSTALL_DEPS" = "n" ] || [ "$INSTALL_DEPS" = "N" ] || [ "$INSTALL_DEPS" = "no" ] || [ "$INSTALL_DEPS" = "No" ]; then
        err "Cannot continue without dependencies. Install them and try again."
    fi

    pkg_update

    for dep in "${MISSING_DEPS[@]}"; do
        case "$dep" in
            java*)   install_java ;;
            pwsh*)   install_pwsh ;;
            curl*)   pkg_install curl ;;
            unzip*)  pkg_install unzip ;;
            git*)    pkg_install git ;;
            fzf*)    pkg_install fzf ;;
            dotnet*) ;;
            aspire*) ;;
        esac
    done

    if [ "$DOTNET_MISSING" = "true" ]; then
        print_sub "Installing .NET 10 SDK..."
        wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
        chmod +x /tmp/dotnet-install.sh
        sudo -u "$CURRENT_USER" bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME_DIR/.dotnet" >/dev/null 2>&1
        ok ".NET 10 installed"
    fi

    if [[ " "${MISSING_DEPS[@]}" " == *"aspire"* ]]; then
        print_sub "Installing .NET Aspire CLI..."
        curl -sSL https://aspire.dev/install.sh | bash >/dev/null 2>&1
        ok ".NET Aspire CLI installed"
    fi

    ok "All dependencies installed"
else
    ok "All dependencies already present"
fi

# ─── STEP 3: INSTALL METHOD CHOICE ─────────────────────────

print_step "INSTALL METHOD"
echo ""
echo -e "${CYN}How would you like to install Solace?${RST}"
echo ""
echo -e "  ${GRN}[1] Prebuilt${RST} - Download a pre-compiled binary"
echo -e "  ${YLW}[2] Source${RST}   - Clone and run source code, no auto update"
echo ""
printf "Choice [1/2] > "
read -r METHOD_CHOICE < /dev/tty
METHOD_CHOICE="$(echo "$METHOD_CHOICE" | tr -d '\r\n')"

case "$METHOD_CHOICE" in
    2|source|Source)
        INSTALL_MODE="source"
        echo -e "${YLW}[INFO] Selected Build from Source${RST}"
        ;;
    *)
        INSTALL_MODE="prebuilt"
        echo -e "${GRN}[INFO] Selected Prebuilt${RST}"
        ;;
esac
echo ""

sudo -u "$CURRENT_USER" mkdir -p "$SOLACE_DIR" 2>/dev/null || mkdir -p "$SOLACE_DIR"

# ─── STEP 4A: PREBUILT PATH ────────────────────────────────

if [ "$INSTALL_MODE" = "prebuilt" ]; then
       echo ""
    echo -e "${CYN}Select branch:${RST}"
    echo ""
    echo -e "  ${GRN}[1] Main (stable - recommended)${RST}"
    echo -e "  ${YLW}[2] Dev (unstable - may break)${RST}"
    echo ""
    printf "Choice [1/2] > "
    read -r BRANCH_CHOICE < /dev/tty
    BRANCH_CHOICE="$(echo "$BRANCH_CHOICE" | tr -d '\r\n')"

   INSTALL_BRANCH="main"
    ARTIFACT_PREFIX="Solace"
    case "$BRANCH_CHOICE" in
        2|dev|Dev) 
            INSTALL_BRANCH="dev"
            ARTIFACT_PREFIX="Solace-Dev"
            ;;
    esac

    if [ "$INSTALL_BRANCH" = "main" ]; then
        print_sub "Fetching available releases..."
        RELEASE_JSON=$(curl -s "https://api.github.com/repos/$GITHUB_REPO/releases/latest")
        SELECTED_TAG=$(echo "$RELEASE_JSON" | grep -o '"tag_name": *"[^"]*"' | sed 's/"tag_name": *"//;s/"//')

        if [ -z "$SELECTED_TAG" ]; then
            RELEASE_JSON=$(curl -s "https://api.github.com/repos/$GITHUB_REPO/releases?per_page=100")
            SELECTED_TAG=$(echo "$RELEASE_JSON" | grep -o '"tag_name": *"[^"]*"' | sed 's/"tag_name": *"//;s/"//' | grep -v "^dev-build$" | head -n1)
        fi

        if [ -z "$SELECTED_TAG" ]; then
            err "No releases found."
        fi
        echo -e "${GRN}Latest version: $SELECTED_TAG${RST}"
    else
        SELECTED_TAG="dev-build"
        echo -e "${YLW}[INFO] Using Dev build${RST}"
    fi

    if [ "$OS" = "Darwin" ]; then
        OS_NAME="macos"
    else
        OS_NAME="linux"
    fi

    ZIP_NAME="${ARTIFACT_PREFIX}-${OS_NAME}-${ARCH_PROFILE}.zip"
    URL="https://github.com/$GITHUB_REPO/releases/download/${SELECTED_TAG}/${ZIP_NAME}"

    echo "[INFO] Downloading $SELECTED_TAG ($ZIP_NAME)..."

    TMP_DIR=$(mktemp -d "/tmp/solace_install_XXXXXX")
    cd "$TMP_DIR"

    if ! curl -L --progress-bar -o server.zip "$URL"; then
        err "Download failed — check your internet or the release URL ($URL)"
    fi
    echo -e "  ${GRN}✔${RST} Download complete"

    print_sub "Extracting..."
    if ! command -v unzip &>/dev/null; then
        err "unzip is not installed — run the installer again to auto-install it"
    fi
    
    mkdir -p "$SERVER_DIR"
    if ! unzip -o server.zip -d "$SERVER_DIR" >/dev/null 2>&1; then
        err "Extraction failed — downloaded file may be corrupted"
    fi

    EXTRACTED_FOLDER="${ARTIFACT_PREFIX}-${MATRIX_OS}-${MATRIX_ARCH}"
    if [ -d "$SERVER_DIR/$EXTRACTED_FOLDER" ]; then
        mv "$SERVER_DIR/$EXTRACTED_FOLDER/"* "$SERVER_DIR/" 2>/dev/null
        rm -rf "$SERVER_DIR/$EXTRACTED_FOLDER"
    fi

    chmod -R +x "$SERVER_DIR/components/" 2>/dev/null || true

    echo "$SELECTED_TAG" > "$VERSION_FILE"
    
    cat > "$SETTINGS_FILE" << JSONEOF
{
  "installMode": "prebuilt",
  "branch": "$INSTALL_BRANCH",
  "version": "$SELECTED_TAG",
  "updatedAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
JSONEOF

    cd /
    rm -rf "$TMP_DIR"
    ok "Solace $SELECTED_TAG downloaded and extracted to $SERVER_DIR"

fi

if [ "$INSTALL_MODE" = "source" ]; then
    print_step "BUILD FROM SOURCE"

    echo ""
    echo -e "${CYN}Select branch:${RST}"
    echo ""
    echo -e "  ${GRN}[1] Main (stable - recommended)${RST}"
    echo -e "  ${YLW}[2] Dev (unstable - may break)${RST}"
    echo ""
    printf "Choice [1/2] > "
    read -r BRANCH_CHOICE < /dev/tty
    BRANCH_CHOICE="$(echo "$BRANCH_CHOICE" | tr -d '\r\n')"

    INSTALL_BRANCH="main"
    case "$BRANCH_CHOICE" in
        2|dev|Dev) INSTALL_BRANCH="dev" ;;
    esac

    command -v git >/dev/null 2>&1 || pkg_install git

    print_sub "Cloning $INSTALL_BRANCH..."
    if [ -d "$SOURCE_DIR/.git" ]; then
        cd "$SOURCE_DIR"
        git remote set-url origin "$GITHUB_URL"
        git fetch origin "$INSTALL_BRANCH"
        git reset --hard "origin/$INSTALL_BRANCH"
        git submodule update --init --recursive
        ok "Repository updated ($INSTALL_BRANCH)"
    else
        rm -rf "$SOURCE_DIR"
        sudo -u "$CURRENT_USER" mkdir -p "$SOURCE_DIR"
        sudo -u "$CURRENT_USER" git clone --recurse-submodules -b "$INSTALL_BRANCH" "$GITHUB_URL" "$SOURCE_DIR"
        cd "$SOURCE_DIR"
        ok "Repository cloned ($INSTALL_BRANCH)"
    fi


    SELECTED_TAG="$INSTALL_BRANCH"
    echo "$SELECTED_TAG" > "$VERSION_FILE"
    cat > "$SETTINGS_FILE" << JSONEOF
{
  "installMode": "source",
  "branch": "$INSTALL_BRANCH",
  "version": "$SELECTED_TAG",
  "updatedAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
JSONEOF
    ok "Solace built from $INSTALL_BRANCH"
fi

chown -R "$CURRENT_USER" "$SOLACE_DIR" 2>/dev/null || true

# ─── STEP 7: INSTALL EARTH COMMAND ─────────────────────────

print_step "INSTALLING EARTH COMMAND"

if [ "$INSTALL_MODE" = "source" ]; then
    EARTH_TARGET_DIR="$SOURCE_DIR/src/Solace.AppHost"
    STATICDATA_PATH="$SOURCE_DIR/staticdata"
    EARTH_INNER_COMMANDS=$(cat << 'CMD'
    echo "Trusting dev certificates..."
    dotnet dev-certs https --trust
    dotnet run
CMD
)
else
    EARTH_TARGET_DIR="$SERVER_DIR/launcher"
    STATICDATA_PATH="$SERVER_DIR/staticdata"
    EARTH_INNER_COMMANDS="./Launcher"
fi

cat << EOF > /tmp/earth
#!/bin/bash
(
    cd "$EARTH_TARGET_DIR" || exit 13

    STATICDATA_PATH="$STATICDATA_PATH"

    SERVER_JAR_NAME="fabric-server-mc.1.20.4-loader.0.15.10-launcher.1.0.1.jar"
    RESOURCENAME="vanilla.zip"
    RESOURCE_DIR="\$STATICDATA_PATH/resourcepacks"
    TEMPLATE_DIR="\$STATICDATA_PATH/server_template_dir"
    MODS_DIR="\$TEMPLATE_DIR/mods"
    EULA_PATH="\$TEMPLATE_DIR/eula.txt"
    RESOURCEPACK_PATH="\$RESOURCE_DIR/\$RESOURCENAME"

    # 1. Resource Pack Check
    if [ ! -f "\$RESOURCEPACK_PATH" ]; then
        echo "ERROR: Resourcepack file '\$RESOURCEPACK_PATH' is missing."
        echo "Download it from: https://cdn.mceserv.net/availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35" - using e.g. https://archive.org
        echo "Rename it to \$RESOURCENAME and move it to: \$RESOURCE_DIR"
        exit 1
    fi

    FILE_SIZE=\$(stat -c%s "\$RESOURCEPACK_PATH" 2>/dev/null || echo 0)

    if [ "\$FILE_SIZE" -lt 100000000 ]; then
        echo "ERROR: Resourcepack file '\$RESOURCEPACK_PATH' is too small or invalid."
        echo "Download it from: https://cdn.mceserv.net/availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35" - using e.g. https://archive.org
        echo "Rename it to \$RESOURCENAME and move it to: \$RESOURCE_DIR"
        exit 1
    fi

    # 2. Fabric API Check
    if ! ls "\$MODS_DIR"/fabric-api-*.jar 1> /dev/null 2>&1; then
        echo "Fabric API not found, downloading..."
        mkdir -p "\$MODS_DIR"
        curl -o "\$MODS_DIR/fabric-api-0.97.0+1.20.4.jar" -L "https://cdn.modrinth.com/data/P7dR8mSH/versions/xklQBMta/fabric-api-0.97.0%2B1.20.4.jar"
        echo "Downloaded fabric api."
    fi

    # 3. Fabric Server Jar Check
    if [ ! -f "\$TEMPLATE_DIR/\$SERVER_JAR_NAME" ]; then
        echo "Fabric server not found, downloading..."
        mkdir -p "\$TEMPLATE_DIR"
        curl -o "\$TEMPLATE_DIR/\$SERVER_JAR_NAME" -L "https://meta.fabricmc.net/v2/versions/loader/1.20.4/0.15.10/1.0.1/server/jar"
        echo "Downloaded fabric server."
    fi

    run_server() {
        echo "Running server..."
        cd "\$TEMPLATE_DIR" || exit
        java -jar "\$SERVER_JAR_NAME" -nogui
        local exit_code=$?
        echo "Server process exited with code \$exit_code"
        return \$exit_code
    }

    # 4. EULA Setup
    if [ ! -f "\$EULA_PATH" ]; then
        run_server
        if [ $? -ne 0 ]; then exit 1; fi
    fi

    # 5. EULA Verification Loop
    if grep -iq "eula=false" "\$EULA_PATH" || ! grep -iq "eula=true" "\$EULA_PATH"; then
        echo "===================================================="
        echo " Minecraft End User License Agreement (EULA)"
        echo "===================================================="
        echo ""
        
        if [ -f "\$EULA_PATH" ]; then
            grep "^#" "\$EULA_PATH"
        else
            echo "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA)."
        fi
        
        echo "===================================================="
        
        while true; do
            read -p "Type 'accept' to accept the EULA and continue: " user_input
            if [ "\${user_input,,}" = "accept" ]; then
                if grep -q "eula=" "\$EULA_PATH"; then
                    sed -i 's/eula=.*/eula=true/I' "\$EULA_PATH"
                else
                    echo "eula=true" >> "\$EULA_PATH"
                fi
                echo "EULA accepted successfully."
                break
            else
                echo "Invalid input. You must type 'accept' to proceed."
            fi
        done
        
        echo "Running server to generate remaining files..."
        run_server
        if [ $? -ne 0 ]; then exit 1; fi
    fi

    cd "$EARTH_TARGET_DIR" || exit 13

$EARTH_INNER_COMMANDS
)
EOF

# Move to the bin directory and make it executable
sudo mv /tmp/earth /usr/local/bin/earth || err "Failed to install earth command"
sudo chmod +x /usr/local/bin/earth

ok "earth command installed (/usr/local/bin/earth)"

# ─── COMPLETE ──────────────────────────────────────────────

echo ""
echo -e "${GRN}========================================${RST}"
echo -e "${ORG}           INSTALL COMPLETE             ${RST}"
echo -e "${GRN}========================================${RST}"
echo ""
echo -e "  ${CYN}User:${RST}    $CURRENT_USER"
echo -e "  ${CYN}OS:${RST}      $OS ($PKG_MANAGER)"
echo -e "  ${CYN}Arch:${RST}    $PROFILE"
echo -e "  ${CYN}Mode:${RST}    $INSTALL_MODE"
echo -e "  ${CYN}Branch:${RST}  $INSTALL_BRANCH"
if [ "$INSTALL_MODE" = "source" ]; then
    echo -e "  ${CYN}Source:${RST}  $SOURCE_DIR"
else
    echo -e "  ${CYN}Server:${RST}  $SERVER_DIR"
fi

if [ "$INSTALL_MODE" = "source" ]; then
    APPSETTINGS_PATH=$SOURCE_DIR/src/Solace.AppHost/appsettings.Development.json
else
    APPSETTINGS_PATH=$SERVER_DIR/launcher/appsettings.json
fi

echo ""
echo -e "${CYN}Next steps:${RST}"
echo -e "  1. Configure options in: ${GRN}$APPSETTINGS_PATH${RST}"
echo "     1.1. Required: BuildplateLauncher/PublicEndPoint - how clients reach the server, without port (e.g., http://example.com or your-ip-address)"
echo "     1.2. Optional: TileRenderer/TileSource - set 'MapTilerApiKey' (get a key at https://cloud.maptiler.com/account/keys/) OR set 'TileDatabaseConnectionString' to a Postgres OSM database"
echo "     1.3. Optional: Database/Earth/UseSqlite - choose between SQLite (true) or a Postgres container (false)"
echo "  2. Download the resource packs (refer to Discord for the commands)"
echo "  3. Run: earth"
echo "  4. Open http://127.0.0.1:5000 and create your admin account"
echo ""
echo -e "${CYN}Useful commands:${RST}"
echo "  earth              TUI menu"
echo ""
