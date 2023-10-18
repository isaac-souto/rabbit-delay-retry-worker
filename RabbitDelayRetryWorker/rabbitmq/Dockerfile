FROM rabbitmq:3.12.6-management

COPY ./plugins/rabbitmq_delayed_message_exchange-3.12.0 /opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange

RUN rabbitmq-plugins enable rabbitmq_shovel rabbitmq_shovel_management rabbitmq_delayed_message_exchange
