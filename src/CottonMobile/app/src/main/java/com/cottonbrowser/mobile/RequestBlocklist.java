package com.cottonbrowser.mobile;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.Reader;
import java.io.StringReader;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.regex.Pattern;

/** Immutable domain engine with conservative EasyList URL/origin exceptions. No Android APIs. */
public final class RequestBlocklist {
    private final DomainBlocklist domains;
    private final List<AllowRule> exceptions;

    private RequestBlocklist(DomainBlocklist domains, List<AllowRule> exceptions) {
        this.domains = domains;
        this.exceptions = Collections.unmodifiableList(exceptions);
    }

    public static RequestBlocklist parse(Reader domains, Reader allows) throws IOException {
        List<AllowRule> exceptions = new ArrayList<>();
        BufferedReader reader = new BufferedReader(allows);
        String line;
        while ((line = reader.readLine()) != null) {
            if (line.isEmpty() || line.startsWith("#")) continue;
            String[] columns = line.split("\t", -1);
            if (columns.length != 5) throw new IOException("Malformed filter exception");
            exceptions.add(new AllowRule(columns));
        }
        return new RequestBlocklist(DomainBlocklist.parse(domains), exceptions);
    }

    public static RequestBlocklist empty() {
        return new RequestBlocklist(DomainBlocklist.empty(), new ArrayList<>());
    }

    public int size() { return domains.size(); }

    /** Slow exception checks run only after the O(host-labels) domain lookup matches. */
    public boolean matches(String url, String requestHost, String originHost) {
        if (url == null || !domains.matchesHost(requestHost)) return false;
        for (AllowRule rule : exceptions) {
            if (rule.matches(url, requestHost, originHost)) return false;
        }
        return true;
    }

    private static DomainBlocklist hosts(String csv) throws IOException {
        return DomainBlocklist.parse(new StringReader(csv.replace(',', '\n')));
    }

    private static final class AllowRule {
        final Pattern url;
        final DomainBlocklist requests, origins, excludedOrigins, excludedRequests;

        AllowRule(String[] columns) throws IOException {
            url = columns[0].isEmpty() ? null : compileUrlFilter(columns[0]);
            requests = hosts(columns[1]);
            origins = hosts(columns[2]);
            excludedOrigins = hosts(columns[3]);
            excludedRequests = hosts(columns[4]);
        }

        boolean matches(String value, String requestHost, String originHost) {
            if (requests.size() != 0 && !requests.matchesHost(requestHost)) return false;
            if (excludedRequests.matchesHost(requestHost)) return false;
            // An absent service-worker origin cannot prove an exception inapplicable.
            if (originHost != null) {
                if (origins.size() != 0 && !origins.matchesHost(originHost)) return false;
                if (excludedOrigins.matchesHost(originHost)) return false;
            }
            return url == null || url.matcher(value).find();
        }
    }

    /** DNR URL tokens: *, ^ separator, | start/end, || domain anchor. */
    static Pattern compileUrlFilter(String filter) {
        StringBuilder regex = new StringBuilder();
        int start = 0;
        if (filter.startsWith("||")) {
            regex.append("^[a-z][a-z0-9+.-]*://(?:[^/?#@]*@)?(?:[^/?#.:]+\\.)*");
            start = 2;
        } else if (filter.startsWith("|")) {
            regex.append('^');
            start = 1;
        }
        for (int i = start; i < filter.length(); i++) {
            char ch = filter.charAt(i);
            if (ch == '*') regex.append(".*");
            else if (ch == '^') regex.append("(?:[^a-z0-9_.%-]|$)");
            else if (ch == '|' && i == filter.length() - 1) regex.append('$');
            else regex.append(Pattern.quote(String.valueOf(ch)));
        }
        return Pattern.compile(regex.toString(), Pattern.CASE_INSENSITIVE);
    }
}
