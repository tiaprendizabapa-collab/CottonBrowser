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
        try {
            return IDN.toASCII(host, IDN.USE_STD3_ASCII_RULES).toLowerCase(Locale.ROOT);
        } catch (IllegalArgumentException ignored) {
            return null;
        }
    }

    private static boolean isValidRule(String domain) {
        if (domain == null || domain.length() > 253 || domain.matches("[0-9.]+")) return false;
        int dot = domain.indexOf('.');
        if (dot <= 0 || dot == domain.length() - 1) return false;
        for (String label : domain.split("\\.", -1)) {
            if (label.isEmpty() || label.length() > 63 || label.startsWith("-") || label.endsWith("-"))
                return false;
        }
        return true;
    }
}
