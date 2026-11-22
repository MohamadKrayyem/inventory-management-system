FROM mcr.microsoft.com/mssql/server:2022-latest

USER root
COPY database.sql phase2.sql phase3.sql /seed/
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
# Strip Windows line endings in case git checked the files out with CRLF
RUN sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh \
    && chmod 755 /usr/local/bin/docker-entrypoint.sh \
    && chmod 644 /seed/*.sql
USER mssql

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
