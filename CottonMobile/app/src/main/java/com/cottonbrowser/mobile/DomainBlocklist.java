package com.cottonbrowser.mobile;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.Reader;
import java.net.IDN;
import java.util.Collections;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;

/** Immutable, Android-independent hosts/domain matcher. */
public final class DomainBlocklist {
    private final Set<String> domains;

    private DomainBlocklist(Set<String> domains) {
        this.domains = Collections.unmodifiableSet(new HashSet<>(domains));
    }

    public static DomainBlocklist empty() {
        return new DomainBlocklist(Collections.emptySet());
    }

    /** Accepts one domain per line or hosts-file entries such as "0.0.0.0 ads.example". */
    public static DomainBlocklist parse(Reader source) throws IOException {
        BufferedReader reader = source instanceof BufferedReader
                ? (BufferedReader) source : new BufferedReader(source);
        Set<String> domains = new HashSet<>();
        String line;
        while ((line = reader.readLine()) != null) {
            int comment = line.indexOf('#');
            if (comment >= 0) line = line.substring(0, comment);
            line = line.trim();
            if (line.isEmpty() || line.startsWith("!")) continue;

            // The APK contains tens of thousands of plain ASCII domains. Avoid regex and
            // IDN allocations for their common path, both at startup and per request.
            if (line.indexOf(' ') < 0 && line.indexOf('\t') < 0) {
                String domain = canonicalHost(line);
                if (isValidRule(domain)) domains.add(domain);
                continue;
            }

            String[] parts = line.split("\\s+");
            int firstDomain = parts.length == 1 ? 0 : (isHostsAddress(parts[0]) ? 1 : -1);
            if (firstDomain < 0) continue;
            for (int i = firstDomain; i < parts.length; i++) {
                String domain = canonicalHost(parts[i]);
                if (isValidRule(domain)) domains.add(domain);
            }
        }
        return new DomainBlocklist(domains);
    }

    public int size() { return domains.size(); }

    /** Matches the exact host or one of its dot-boundary parent domains. */
    public boolean matchesHost(String rawHost) {
        String host = canonicalHost(rawHost);
        if (host == null) return false;
        if (domains.contains(host)) return true;
        for (int dot = host.indexOf('.'); dot >= 0; dot = host.indexOf('.', dot + 1)) {
            if (domains.contains(host.substring(dot + 1))) return true;
        }
        return false;
    }

    private static boolean isHostsAddress(String value) {
        return value.indexOf(':') >= 0 || value.matches("[0-9.]+");
    }

    private static String canonicalHost(String value) {
        if (value == null) return null;
        String host = value.trim();
        if (host.endsWith(".")) host = host.substring(0, host.length() - 1);
        boolean ascii = true;
        for (int i = 0; i < host.length(); i++) {
            if (host.charAt(i) > 127) { ascii = false; break; }
        }
        if (ascii) return isValidRule(host) ? host.toLowerCase(Locale.ROOT) : null;
        try {
            return IDN.toASCII(host, IDN.USE_STD3_ASCII_RULES).toLowerCase(Locale.ROOT);
        } catch (IllegalArgumentException ignored) {
            return null;
        }
    }

    private static boolean isValidRule(String domain) {
        if (domain == null || domain.length() > 253) return false;
        int dot = domain.indexOf('.');
        if (dot <= 0 || dot == domain.length() - 1) return false;
        int start = 0;
        boolean allNumeric = true;
        for (int i = 0; i <= domain.length(); i++) {
            if (i == domain.length() || domain.charAt(i) == '.') {
                if (i == start || i - start > 63 || domain.charAt(start) == '-' || domain.charAt(i - 1) == '-')
                    return false;
                start = i + 1;
            } else {
                char ch = domain.charAt(i);
                boolean digit = ch >= '0' && ch <= '9';
                if (!digit && ch != '-' && !(ch >= 'a' && ch <= 'z') && !(ch >= 'A' && ch <= 'Z'))
                    return false;
                if (!digit) allNumeric = false;
            }
        }
        return !allNumeric;
    }
}
